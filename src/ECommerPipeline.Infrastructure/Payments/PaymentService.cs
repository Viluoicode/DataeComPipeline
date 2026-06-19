using ECommerPipeline.Application.Payments;
using ECommerPipeline.Domain.Entities;
using ECommerPipeline.Domain.Enums;
using ECommerPipeline.Infrastructure.Notifications;
using ECommerPipeline.Infrastructure.Observability;
using ECommerPipeline.Infrastructure.Persistence.Oltp;
using Microsoft.EntityFrameworkCore;

namespace ECommerPipeline.Infrastructure.Payments;

public class PaymentService : IPaymentService
{
    private readonly OltpDbContext _db;
    private readonly IReadOnlyDictionary<PaymentMethod, IPaymentGateway> _gateways;
    private readonly BusinessMetrics? _metrics;

    // BusinessMetrics is optional so direct unit-test construction stays simple.
    public PaymentService(
        OltpDbContext db, IEnumerable<IPaymentGateway> gateways, BusinessMetrics? metrics = null)
    {
        _db = db;
        _gateways = gateways.ToDictionary(g => g.Method);
        _metrics = metrics;
    }

    public async Task<PaymentCreatedResponse> CreateAsync(
        long orderId, long customerId, string ipAddress, CancellationToken ct = default)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new KeyNotFoundException($"Order {orderId} not found.");

        if (order.CustomerId != customerId)
            throw new UnauthorizedAccessException("You can only pay for your own orders.");
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Delivered)
            throw new InvalidOperationException($"Order in status '{order.Status}' cannot be paid.");
        if (order.PaymentStatus == PaymentStatus.Paid)
            throw new InvalidOperationException("Order is already paid.");
        if (order.PaymentMethod == PaymentMethod.Cod)
            throw new InvalidOperationException("COD orders do not require online payment.");

        if (!_gateways.TryGetValue(order.PaymentMethod, out var gateway))
            throw new InvalidOperationException($"No payment gateway for '{order.PaymentMethod}'.");

        // Unique per-attempt reference the gateway echoes back in its callbacks.
        var providerTxnRef = $"{orderId}{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        var payment = new Payment
        {
            OrderId = order.Id,
            Provider = order.PaymentMethod,
            Amount = order.TotalAmount,
            ProviderTxnRef = providerTxnRef,
            Status = PaymentStatus.Pending,
        };
        _db.Payments.Add(payment);
        order.PaymentStatus = PaymentStatus.Pending;
        await _db.SaveChangesAsync(ct);

        var ctx = new PaymentInitContext(
            order.Id, order.OrderNumber, providerTxnRef, order.TotalAmount, ipAddress);
        var init = await gateway.CreatePaymentAsync(ctx, ct);

        return new PaymentCreatedResponse(init.RedirectUrl, providerTxnRef);
    }

    public async Task<PaymentResultDto> HandleCallbackAsync(
        PaymentMethod method, IReadOnlyDictionary<string, string> data, CancellationToken ct = default)
    {
        if (!_gateways.TryGetValue(method, out var gateway))
            throw new InvalidOperationException($"No payment gateway for '{method}'.");

        var cb = gateway.VerifyCallback(data);

        var payment = await _db.Payments
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.ProviderTxnRef == cb.ProviderTxnRef, ct);

        if (payment is null)
            return new PaymentResultDto(false, null, null, PaymentStatus.Failed, "Unknown payment reference.");

        var order = payment.Order;

        // Idempotency: a replayed callback for an already-settled payment is a no-op.
        if (payment.Status == PaymentStatus.Paid)
            return new PaymentResultDto(true, order.Id, order.OrderNumber, PaymentStatus.Paid, "Already processed.");

        payment.RawCallback = cb.RawData;

        // Never act on data whose signature does not validate.
        if (!cb.SignatureValid)
            return new PaymentResultDto(false, order.Id, order.OrderNumber, payment.Status, "Invalid signature.");

        // Guard against amount tampering — only honour the exact reserved amount.
        if (cb.Success && cb.Amount != payment.Amount)
        {
            payment.Status = PaymentStatus.Failed;
            payment.ResponseCode = cb.ResponseCode;
            if (order.PaymentStatus == PaymentStatus.Pending) order.PaymentStatus = PaymentStatus.Failed;
            await _db.SaveChangesAsync(ct);
            _metrics?.PaymentOutcome(false);
            return new PaymentResultDto(false, order.Id, order.OrderNumber, PaymentStatus.Failed, "Amount mismatch.");
        }

        if (cb.Success)
        {
            payment.Status = PaymentStatus.Paid;
            payment.GatewayTransactionId = cb.GatewayTransactionId;
            payment.ResponseCode = cb.ResponseCode;
            payment.PaidAt = DateTime.UtcNow;
            order.PaymentStatus = PaymentStatus.Paid;

            // A paid online order is auto-confirmed (Pending → Confirmed).
            if (order.Status == OrderStatus.Pending)
            {
                order.Status = OrderStatus.Confirmed;
                order.UpdatedAt = DateTime.UtcNow;
                order.Events.Add(new OrderEvent
                {
                    FromStatus = OrderStatus.Pending,
                    ToStatus = OrderStatus.Confirmed,
                    ActorCustomerId = null, // system
                    Reason = $"Auto-confirmed after {method} payment"
                });
            }

            // Transactional outbox → "payment succeeded" email + in-app notification.
            _db.OutboxMessages.Add(new OutboxMessage
            {
                OrderId = order.Id,
                EventType = OutboxEventTypes.PaymentSucceeded,
            });

            await _db.SaveChangesAsync(ct);
            _metrics?.PaymentOutcome(true);
            return new PaymentResultDto(true, order.Id, order.OrderNumber, PaymentStatus.Paid, "Payment successful.");
        }

        // Failed / cancelled at the gateway.
        payment.Status = PaymentStatus.Failed;
        payment.ResponseCode = cb.ResponseCode;
        if (order.PaymentStatus == PaymentStatus.Pending) order.PaymentStatus = PaymentStatus.Failed;
        await _db.SaveChangesAsync(ct);
        _metrics?.PaymentOutcome(false);
        return new PaymentResultDto(false, order.Id, order.OrderNumber, PaymentStatus.Failed, "Payment failed or cancelled.");
    }
}

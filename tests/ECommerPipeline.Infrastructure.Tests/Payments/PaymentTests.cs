using System.Net;
using System.Security.Cryptography;
using System.Text;
using ECommerPipeline.Application.Payments;
using ECommerPipeline.Domain.Entities;
using ECommerPipeline.Domain.Enums;
using ECommerPipeline.Infrastructure.Payments;
using ECommerPipeline.Infrastructure.Persistence.Oltp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace ECommerPipeline.Infrastructure.Tests.Payments;

public class VnPayGatewayTests
{
    private const string Secret = "VNPAY_TEST_HASH_SECRET_0123456789";

    private static VnPayGateway NewGateway() =>
        new(Options.Create(new PaymentOptions
        {
            VnPay = new VnPayOptions { TmnCode = "TEST01", HashSecret = Secret }
        }));

    // Mirrors the gateway's signing so we can forge a *valid* callback and prove
    // that (a) a correctly-signed success is accepted and (b) any tampering is rejected.
    private static string Sign(SortedDictionary<string, string> p)
    {
        var sb = new StringBuilder();
        foreach (var (k, v) in p)
        {
            if (string.IsNullOrEmpty(v)) continue;
            if (sb.Length > 0) sb.Append('&');
            sb.Append(WebUtility.UrlEncode(k)).Append('=').Append(WebUtility.UrlEncode(v));
        }
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(Secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    private static Dictionary<string, string> SignedSuccessCallback()
    {
        var signed = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Amount"] = "5000000",
            ["vnp_ResponseCode"] = "00",
            ["vnp_TransactionStatus"] = "00",
            ["vnp_TxnRef"] = "12345",
            ["vnp_TransactionNo"] = "987654",
        };
        var data = new Dictionary<string, string>(signed) { ["vnp_SecureHash"] = Sign(signed) };
        return data;
    }

    [Fact]
    public void VerifyCallback_accepts_valid_signed_success()
    {
        var result = NewGateway().VerifyCallback(SignedSuccessCallback());

        result.SignatureValid.Should().BeTrue();
        result.Success.Should().BeTrue();
        result.ProviderTxnRef.Should().Be("12345");
        result.Amount.Should().Be(50_000m); // vnp_Amount / 100
    }

    [Fact]
    public void VerifyCallback_rejects_tampered_amount()
    {
        var data = SignedSuccessCallback();
        data["vnp_Amount"] = "1"; // tamper after signing

        var result = NewGateway().VerifyCallback(data);

        result.SignatureValid.Should().BeFalse();
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void VerifyCallback_rejects_missing_hash()
    {
        var data = SignedSuccessCallback();
        data.Remove("vnp_SecureHash");

        NewGateway().VerifyCallback(data).SignatureValid.Should().BeFalse();
    }
}

public class PaymentServiceTests
{
    private static OltpDbContext NewContext() =>
        new(new DbContextOptionsBuilder<OltpDbContext>()
            .UseInMemoryDatabase($"pay-{Guid.NewGuid()}").Options);

    // Stub gateway returning a fixed verified callback — lets us exercise the
    // service's settlement + idempotency logic without real HTTP/crypto.
    private sealed class StubGateway(PaymentCallbackResult result) : IPaymentGateway
    {
        public PaymentMethod Method => PaymentMethod.VnPay;
        public Task<PaymentInitResult> CreatePaymentAsync(PaymentInitContext ctx, CancellationToken ct = default)
            => Task.FromResult(new PaymentInitResult("https://gw/redirect"));
        public PaymentCallbackResult VerifyCallback(IReadOnlyDictionary<string, string> data) => result;
    }

    private static async Task<(OltpDbContext db, Order order, Payment payment)> SeedAsync(decimal amount = 50_000m)
    {
        var db = NewContext();
        var order = new Order
        {
            OrderNumber = "ORD-1", CustomerId = 1, OrderDate = DateTime.UtcNow,
            Status = OrderStatus.Pending, PaymentMethod = PaymentMethod.VnPay,
            PaymentStatus = PaymentStatus.Pending, TotalAmount = amount,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        var payment = new Payment
        {
            OrderId = order.Id, Provider = PaymentMethod.VnPay, Amount = amount,
            ProviderTxnRef = "REF1", Status = PaymentStatus.Pending,
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        return (db, order, payment);
    }

    private static PaymentCallbackResult Success(decimal amount) =>
        new(SignatureValid: true, Success: true, ProviderTxnRef: "REF1",
            GatewayTransactionId: "GW1", Amount: amount, ResponseCode: "00", RawData: "{}");

    [Fact]
    public async Task HandleCallback_marks_paid_and_auto_confirms()
    {
        var (db, _, _) = await SeedAsync();
        var sut = new PaymentService(db, new[] { new StubGateway(Success(50_000m)) });

        var result = await sut.HandleCallbackAsync(PaymentMethod.VnPay, new Dictionary<string, string>());

        result.Success.Should().BeTrue();
        var order = await db.Orders.SingleAsync();
        order.PaymentStatus.Should().Be(PaymentStatus.Paid);
        order.Status.Should().Be(OrderStatus.Confirmed);      // auto-confirmed
        (await db.Payments.SingleAsync()).Status.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public async Task HandleCallback_is_idempotent_on_replay()
    {
        var (db, _, _) = await SeedAsync();
        var sut = new PaymentService(db, new[] { new StubGateway(Success(50_000m)) });

        await sut.HandleCallbackAsync(PaymentMethod.VnPay, new Dictionary<string, string>());
        var second = await sut.HandleCallbackAsync(PaymentMethod.VnPay, new Dictionary<string, string>());

        second.Success.Should().BeTrue();
        (await db.Payments.CountAsync()).Should().Be(1);
        (await db.Payments.SingleAsync()).Status.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public async Task HandleCallback_rejects_amount_mismatch()
    {
        var (db, _, _) = await SeedAsync(amount: 50_000m);
        // Gateway reports a different amount than the reserved one.
        var sut = new PaymentService(db, new[] { new StubGateway(Success(1m)) });

        var result = await sut.HandleCallbackAsync(PaymentMethod.VnPay, new Dictionary<string, string>());

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Amount mismatch");
        (await db.Orders.SingleAsync()).PaymentStatus.Should().Be(PaymentStatus.Failed);
    }
}

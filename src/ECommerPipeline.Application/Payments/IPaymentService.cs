using ECommerPipeline.Domain.Enums;

namespace ECommerPipeline.Application.Payments;

/// Orchestrates online payment: creating a gateway redirect for an order and
/// applying gateway callbacks (idempotently) to the Payment + Order records.
public interface IPaymentService
{
    /// Start an online payment for <paramref name="orderId"/>. Verifies the order
    /// belongs to <paramref name="customerId"/>, is payable, and uses an online
    /// method, then returns the gateway redirect URL.
    Task<PaymentCreatedResponse> CreateAsync(
        long orderId, long customerId, string ipAddress, CancellationToken ct = default);

    /// Apply a verified gateway callback. Idempotent: a replayed callback for an
    /// already-paid order is a no-op success (no double-credit).
    Task<PaymentResultDto> HandleCallbackAsync(
        PaymentMethod method, IReadOnlyDictionary<string, string> data, CancellationToken ct = default);
}

public record PaymentCreatedResponse(string RedirectUrl, string ProviderTxnRef);

public record PaymentResultDto(
    bool Success,
    long? OrderId,
    string? OrderNumber,
    PaymentStatus PaymentStatus,
    string Message);

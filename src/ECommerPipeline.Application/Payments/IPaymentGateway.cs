using ECommerPipeline.Domain.Enums;

namespace ECommerPipeline.Application.Payments;

/// A payment provider adapter (VNPay, MoMo, …). Each implementation handles one
/// <see cref="PaymentMethod"/>. Keeping this provider-agnostic means switching
/// from sandbox to a live merchant account is just a config change.
public interface IPaymentGateway
{
    PaymentMethod Method { get; }

    /// Build (or request) the gateway redirect URL the customer is sent to.
    Task<PaymentInitResult> CreatePaymentAsync(PaymentInitContext ctx, CancellationToken ct = default);

    /// Verify a gateway callback (browser return URL or server-to-server IPN).
    /// Pure + side-effect-free: signature check + field extraction only. The
    /// service decides what to persist. Never trust callback data whose signature
    /// fails to validate.
    PaymentCallbackResult VerifyCallback(IReadOnlyDictionary<string, string> data);
}

public record PaymentInitContext(
    long OrderId,
    string OrderNumber,
    string ProviderTxnRef,
    decimal Amount,
    string IpAddress,
    string Locale = "vn");

public record PaymentInitResult(string RedirectUrl);

public record PaymentCallbackResult(
    bool SignatureValid,
    bool Success,
    string ProviderTxnRef,
    string? GatewayTransactionId,
    decimal Amount,
    string ResponseCode,
    string RawData);

namespace ECommerPipeline.Infrastructure.Payments;

/// Bound from the "Payments" config section. Secrets (HashSecret / SecretKey)
/// come from env vars in Docker/prod, never committed. Sandbox is free to use
/// without a business licence; go-live only swaps these credentials.
public class PaymentOptions
{
    public const string SectionName = "Payments";

    /// Absolute URL the browser is sent to after the gateway finishes processing
    /// (the SPA result page). e.g. http://localhost:5173/payment-result for local
    /// dev, or "/payment-result" when API + SPA share an origin (Docker/nginx).
    public string FrontendResultUrl { get; set; } = "/payment-result";

    public VnPayOptions VnPay { get; set; } = new();
    public MomoOptions Momo { get; set; } = new();
}

public class VnPayOptions
{
    public string TmnCode { get; set; } = "";
    public string HashSecret { get; set; } = "";
    public string BaseUrl { get; set; } = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";

    /// Where VNPay sends the customer's browser back to (must hit our API).
    public string ReturnUrl { get; set; } = "http://localhost:5193/api/payments/vnpay/return";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(TmnCode) && !string.IsNullOrWhiteSpace(HashSecret);
}

public class MomoOptions
{
    public string PartnerCode { get; set; } = "";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string CreateUrl { get; set; } = "https://test-payment.momo.vn/v2/gateway/api/create";

    /// Browser redirect target after MoMo finishes (our API return endpoint).
    public string ReturnUrl { get; set; } = "http://localhost:5193/api/payments/momo/return";

    /// Server-to-server IPN target (must be public for MoMo to reach it).
    public string IpnUrl { get; set; } = "http://localhost:5193/api/payments/momo/ipn";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PartnerCode) &&
        !string.IsNullOrWhiteSpace(AccessKey) &&
        !string.IsNullOrWhiteSpace(SecretKey);
}

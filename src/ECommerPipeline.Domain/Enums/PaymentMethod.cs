namespace ECommerPipeline.Domain.Enums;

/// How the customer chose to pay. COD requires no gateway; VnPay/Momo redirect
/// to an external payment page (see Infrastructure/Payments, Phase 2).
public enum PaymentMethod
{
    Cod = 1,    // cash on delivery — no online gateway
    VnPay = 2,
    Momo = 3
}

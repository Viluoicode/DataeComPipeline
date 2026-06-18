namespace ECommerPipeline.Domain.Enums;

/// Payment lifecycle for an order, independent of fulfilment (OrderStatus).
/// COD orders stay <see cref="Unpaid"/> until delivered; online orders move
/// Pending → Paid (or Failed) via the payment gateway callback/IPN.
public enum PaymentStatus
{
    Unpaid = 1,   // no payment expected yet (e.g. COD not collected)
    Pending = 2,  // online payment initiated, awaiting gateway confirmation
    Paid = 3,     // gateway confirmed funds captured
    Failed = 4,   // gateway reported failure / user abandoned
    Refunded = 5  // money returned after a paid order was cancelled
}

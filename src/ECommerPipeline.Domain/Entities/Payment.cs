using ECommerPipeline.Domain.Common;
using ECommerPipeline.Domain.Enums;

namespace ECommerPipeline.Domain.Entities;

/// One attempt to pay an order through an online gateway (VNPay/MoMo). The
/// authoritative payment record — gateway callbacks (return URL + server-to-server
/// IPN) are matched back to it by <see cref="ProviderTxnRef"/> and applied
/// idempotently so a replayed IPN can never double-credit an order.
public class Payment : BaseEntity
{
    public long OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public PaymentMethod Provider { get; set; }
    public decimal Amount { get; set; }

    /// Our unique reference sent to the gateway (VNPay vnp_TxnRef / MoMo orderId).
    public string ProviderTxnRef { get; set; } = null!;

    /// The gateway's own transaction id, returned on success (vnp_TransactionNo / MoMo transId).
    public string? GatewayTransactionId { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? ResponseCode { get; set; }

    /// Raw callback payload kept for audit / dispute resolution.
    public string? RawCallback { get; set; }

    public DateTime? PaidAt { get; set; }
}

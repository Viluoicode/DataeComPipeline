using ECommerPipeline.Domain.Common;
using ECommerPipeline.Domain.Enums;

namespace ECommerPipeline.Domain.Entities;

/// Immutable record of a single order-status transition. One row per change,
/// appended whenever an order moves between states. Feeds the order-tracking
/// timeline (frontend) and the conversion-funnel analytics in the Gold layer.
public class OrderEvent : BaseEntity
{
    public long OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public OrderStatus? FromStatus { get; set; }   // null = order creation
    public OrderStatus ToStatus { get; set; }

    /// Customer id of whoever triggered the change (admin/staff/customer).
    /// Null for system-driven transitions (e.g. payment IPN).
    public long? ActorCustomerId { get; set; }

    public string? Reason { get; set; }
}

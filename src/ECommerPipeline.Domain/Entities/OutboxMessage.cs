using ECommerPipeline.Domain.Common;

namespace ECommerPipeline.Domain.Entities;

/// Transactional outbox row. Written in the SAME SaveChanges as the order/payment
/// change that triggered it, so a notification can never be lost (or sent for a
/// change that rolled back). A background dispatcher later renders + sends the
/// email and pushes the in-app SignalR notification, then stamps ProcessedAt.
public class OutboxMessage : BaseEntity
{
    public string EventType { get; set; } = null!;

    public long OrderId { get; set; }
    public Order? Order { get; set; }

    /// Optional JSON context (e.g. from/to status for a transition).
    public string Payload { get; set; } = "{}";

    public DateTime? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

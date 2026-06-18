namespace ECommerPipeline.Infrastructure.Notifications;

/// Notification event kinds carried by an OutboxMessage.EventType.
public static class OutboxEventTypes
{
    public const string OrderPlaced        = "OrderPlaced";
    public const string PaymentSucceeded   = "PaymentSucceeded";
    public const string OrderStatusChanged = "OrderStatusChanged";
}

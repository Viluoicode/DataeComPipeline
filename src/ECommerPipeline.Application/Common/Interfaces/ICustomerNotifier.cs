namespace ECommerPipeline.Application.Common.Interfaces;

/// Pushes an in-app realtime notification to a specific customer (SignalR group).
/// Abstraction so Infrastructure (the outbox dispatcher) can notify without
/// depending on SignalR; implemented in the Api layer. Mirrors <see cref="IEtlNotifier"/>.
public interface ICustomerNotifier
{
    Task NotifyOrderAsync(long customerId, OrderNotification notification, CancellationToken ct = default);
}

public record OrderNotification(
    long OrderId,
    string OrderNumber,
    int Status,
    int PaymentStatus,
    string Message);

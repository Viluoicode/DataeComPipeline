namespace ECommerPipeline.Domain.Enums;

/// Single source of truth for the order-fulfilment state machine. Prevents
/// invalid jumps (e.g. Delivered → Pending, or reviving a Cancelled order).
public static class OrderStatusTransitions
{
    private static readonly IReadOnlyDictionary<OrderStatus, OrderStatus[]> Allowed =
        new Dictionary<OrderStatus, OrderStatus[]>
        {
            [OrderStatus.Pending]   = new[] { OrderStatus.Confirmed, OrderStatus.Cancelled },
            [OrderStatus.Confirmed] = new[] { OrderStatus.Shipped,   OrderStatus.Cancelled },
            [OrderStatus.Shipped]   = new[] { OrderStatus.Delivered },
            [OrderStatus.Delivered] = Array.Empty<OrderStatus>(),  // terminal
            [OrderStatus.Cancelled] = Array.Empty<OrderStatus>(),  // terminal
        };

    public static bool CanTransition(OrderStatus from, OrderStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static IReadOnlyList<OrderStatus> NextStates(OrderStatus from) =>
        Allowed.TryGetValue(from, out var targets) ? targets : Array.Empty<OrderStatus>();
}

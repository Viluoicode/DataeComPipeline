using ECommerPipeline.Application.Common.DTOs;
using ECommerPipeline.Application.Orders.DTOs;
using ECommerPipeline.Domain.Enums;

namespace ECommerPipeline.Application.Orders;

public interface IOrderService
{
    Task<OrderCreatedResponse> CreateAsync(CreateOrderRequest request, CancellationToken ct = default);
    Task<PagedResult<OrderListItemDto>> GetPagedAsync(OrderQueryParams query, CancellationToken ct = default);
    Task<OrderDetailDto?> GetByIdAsync(long id, CancellationToken ct = default);

    /// Staff/Admin advance an order along the fulfilment state machine.
    /// Throws if the transition is invalid. Restocks items when cancelled.
    Task<OrderDetailDto> UpdateStatusAsync(
        long id, OrderStatus newStatus, long? actorCustomerId, string? reason, CancellationToken ct = default);

    /// Customer cancels their own order while it is still Pending. Restocks items.
    Task<OrderDetailDto> CancelByCustomerAsync(
        long id, long customerId, string? reason, CancellationToken ct = default);
}

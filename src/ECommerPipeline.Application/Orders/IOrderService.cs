using ECommerPipeline.Application.Common.DTOs;
using ECommerPipeline.Application.Orders.DTOs;

namespace ECommerPipeline.Application.Orders;

public interface IOrderService
{
    Task<OrderCreatedResponse> CreateAsync(CreateOrderRequest request, CancellationToken ct = default);
    Task<PagedResult<OrderListItemDto>> GetPagedAsync(OrderQueryParams query, CancellationToken ct = default);
    Task<OrderDetailDto?> GetByIdAsync(long id, CancellationToken ct = default);
}

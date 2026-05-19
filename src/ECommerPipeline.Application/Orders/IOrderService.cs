using ECommerPipeline.Application.Orders.DTOs;

namespace ECommerPipeline.Application.Orders;

public interface IOrderService
{
    Task<OrderCreatedResponse> CreateAsync(CreateOrderRequest request, CancellationToken ct = default);
}

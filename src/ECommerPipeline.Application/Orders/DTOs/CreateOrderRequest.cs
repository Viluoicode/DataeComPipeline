namespace ECommerPipeline.Application.Orders.DTOs;

public record CreateOrderRequest(long CustomerId, List<CreateOrderItem> Items);

public record CreateOrderItem(long ProductId, int Quantity);

public record OrderCreatedResponse(long OrderId, string OrderNumber, decimal TotalAmount);

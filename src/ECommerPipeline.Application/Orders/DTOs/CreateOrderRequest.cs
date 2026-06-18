using ECommerPipeline.Domain.Enums;

namespace ECommerPipeline.Application.Orders.DTOs;

public record CreateOrderRequest(
    long CustomerId,
    List<CreateOrderItem> Items,
    string? ShipFullName = null,
    string? ShipPhone = null,
    string? ShipAddress = null,
    string? Note = null,
    PaymentMethod PaymentMethod = PaymentMethod.Cod);

public record CreateOrderItem(long ProductId, int Quantity);

public record OrderCreatedResponse(
    long OrderId,
    string OrderNumber,
    decimal TotalAmount,
    PaymentMethod PaymentMethod,
    PaymentStatus PaymentStatus);

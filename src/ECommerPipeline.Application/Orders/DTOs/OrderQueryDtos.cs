using ECommerPipeline.Domain.Enums;

namespace ECommerPipeline.Application.Orders.DTOs;

public record OrderQueryParams(
    int Page = 1,
    int PageSize = 20,
    OrderStatus? Status = null,
    long? CustomerId = null,
    DateTime? From = null,
    DateTime? To = null,
    string? Search = null);

public record OrderListItemDto(
    long Id,
    string OrderNumber,
    long CustomerId,
    string CustomerName,
    DateTime OrderDate,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    decimal TotalAmount,
    int ItemCount);

public record OrderDetailDto(
    long Id,
    string OrderNumber,
    long CustomerId,
    string CustomerName,
    string CustomerEmail,
    DateTime OrderDate,
    OrderStatus Status,
    PaymentMethod PaymentMethod,
    PaymentStatus PaymentStatus,
    string? ShipFullName,
    string? ShipPhone,
    string? ShipAddress,
    string? Note,
    decimal Subtotal,
    decimal ShippingFee,
    decimal TaxAmount,
    decimal TotalAmount,
    IReadOnlyList<OrderItemDetailDto> Items,
    IReadOnlyList<OrderEventDto> Events,
    IReadOnlyList<OrderStatus> NextStatuses);

public record OrderItemDetailDto(
    long ProductId,
    string ProductSku,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public record OrderEventDto(
    OrderStatus? FromStatus,
    OrderStatus ToStatus,
    long? ActorCustomerId,
    string? Reason,
    DateTime At);

/// Staff/Admin advance an order to the next valid fulfilment state.
public record UpdateOrderStatusRequest(OrderStatus Status, string? Reason = null);

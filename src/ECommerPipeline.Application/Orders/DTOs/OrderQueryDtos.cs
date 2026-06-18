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
    decimal TotalAmount,
    IReadOnlyList<OrderItemDetailDto> Items);

public record OrderItemDetailDto(
    long ProductId,
    string ProductSku,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

using ECommerPipeline.Application.Common.Interfaces;
using ECommerPipeline.Application.Orders;
using ECommerPipeline.Application.Orders.DTOs;
using ECommerPipeline.Domain.Entities;
using ECommerPipeline.Domain.Enums;
using ECommerPipeline.Infrastructure.Persistence.Oltp;
using Microsoft.EntityFrameworkCore;

namespace ECommerPipeline.Infrastructure.Orders;

public class OrderService : IOrderService
{
    private readonly OltpDbContext _db;

    public OrderService(OltpDbContext db) => _db = db;

    public async Task<OrderCreatedResponse> CreateAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        var productIds = request.Items.Select(i => i.ProductId).ToArray();
        var products = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        if (products.Count != productIds.Length)
            throw new InvalidOperationException("One or more products not found.");

        var order = new Order
        {
            OrderNumber = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
            CustomerId = request.CustomerId,
            OrderDate = DateTime.UtcNow,
            Status = OrderStatus.Pending,
            Items = request.Items.Select(i =>
            {
                var p = products[i.ProductId];
                return new OrderItem
                {
                    ProductId = p.Id,
                    Quantity = i.Quantity,
                    UnitPrice = p.Price,
                    LineTotal = p.Price * i.Quantity
                };
            }).ToList()
        };
        order.TotalAmount = order.Items.Sum(i => i.LineTotal);

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);

        return new OrderCreatedResponse(order.Id, order.OrderNumber, order.TotalAmount);
    }
}

using ECommerPipeline.Application.Common.DTOs;
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

    public async Task<PagedResult<OrderListItemDto>> GetPagedAsync(OrderQueryParams q, CancellationToken ct = default)
    {
        var page     = q.Page     <= 0 ? 1  : q.Page;
        var pageSize = q.PageSize <= 0 ? 20 : Math.Min(q.PageSize, 200);

        var query = _db.Orders.AsNoTracking().AsQueryable();

        if (q.Status.HasValue)    query = query.Where(o => o.Status == q.Status.Value);
        if (q.CustomerId.HasValue) query = query.Where(o => o.CustomerId == q.CustomerId.Value);
        if (q.From.HasValue)       query = query.Where(o => o.OrderDate >= q.From.Value);
        if (q.To.HasValue)         query = query.Where(o => o.OrderDate <= q.To.Value);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            query = query.Where(o => o.OrderNumber.Contains(s) || o.Customer.FullName.Contains(s));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(o => o.OrderDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new OrderListItemDto(
                o.Id,
                o.OrderNumber,
                o.CustomerId,
                o.Customer.FullName,
                o.OrderDate,
                o.Status,
                o.TotalAmount,
                o.Items.Count))
            .ToListAsync(ct);

        return new PagedResult<OrderListItemDto>(items, page, pageSize, total);
    }

    public async Task<OrderDetailDto?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        return await _db.Orders.AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => new OrderDetailDto(
                o.Id,
                o.OrderNumber,
                o.CustomerId,
                o.Customer.FullName,
                o.Customer.Email,
                o.OrderDate,
                o.Status,
                o.TotalAmount,
                o.Items.Select(i => new OrderItemDetailDto(
                    i.ProductId,
                    i.Product.Sku,
                    i.Product.Name,
                    i.Quantity,
                    i.UnitPrice,
                    i.LineTotal
                )).ToList()))
            .FirstOrDefaultAsync(ct);
    }
}

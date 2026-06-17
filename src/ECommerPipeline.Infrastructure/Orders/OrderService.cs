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
        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToArray();
        // Tracked (not AsNoTracking) — we decrement StockQuantity on these entities
        // and persist them together with the order in a single atomic SaveChanges.
        var products = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        if (products.Count != productIds.Length)
            throw new InvalidOperationException("One or more products not found.");

        // An order may list the same product on multiple lines — reserve the total.
        var qtyByProduct = request.Items
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        // Reserve stock. Concurrent oversell is caught at save time by the Product
        // rowversion concurrency token (DbUpdateConcurrencyException → retry).
        foreach (var (productId, qty) in qtyByProduct)
        {
            var p = products[productId];
            if (p.StockQuantity < qty)
                throw new InvalidOperationException($"Insufficient stock for product '{p.Name}'.");
            p.StockQuantity -= qty;
        }

        var order = new Order
        {
            OrderNumber = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
            CustomerId = request.CustomerId,
            OrderDate = DateTime.UtcNow,
            Status = OrderStatus.Pending,
            PaymentMethod = request.PaymentMethod,
            PaymentStatus = PaymentStatus.Unpaid,   // online flow flips to Pending/Paid in Phase 2
            ShipFullName = request.ShipFullName,
            ShipPhone = request.ShipPhone,
            ShipAddress = request.ShipAddress,
            Note = request.Note,
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
        order.Events.Add(new OrderEvent
        {
            FromStatus = null,
            ToStatus = OrderStatus.Pending,
            ActorCustomerId = request.CustomerId,
            Reason = "Order created"
        });

        _db.Orders.Add(order);
        await SaveWithConcurrencyGuardAsync(ct);

        return new OrderCreatedResponse(
            order.Id, order.OrderNumber, order.TotalAmount, order.PaymentMethod, order.PaymentStatus);
    }

    public async Task<PagedResult<OrderListItemDto>> GetPagedAsync(OrderQueryParams q, CancellationToken ct = default)
    {
        var page     = q.Page     <= 0 ? 1  : q.Page;
        var pageSize = q.PageSize <= 0 ? 20 : Math.Min(q.PageSize, 200);

        try
        {
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
                    o.PaymentStatus,
                    o.TotalAmount,
                    o.Items.Count))
                .ToListAsync(ct);

            return new PagedResult<OrderListItemDto>(items, page, pageSize, total);
        }
        catch (OperationCanceledException) { throw; } // ack cancellation for VS debugger
    }

    public async Task<OrderDetailDto?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        try
        {
            var order = await _db.Orders.AsNoTracking()
                .Include(o => o.Customer)
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Include(o => o.Events)
                .FirstOrDefaultAsync(o => o.Id == id, ct);

            return order is null ? null : MapDetail(order);
        }
        catch (OperationCanceledException) { throw; }
    }

    public async Task<OrderDetailDto> UpdateStatusAsync(
        long id, OrderStatus newStatus, long? actorCustomerId, string? reason, CancellationToken ct = default)
    {
        var order = await _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new KeyNotFoundException($"Order {id} not found.");

        await TransitionAsync(order, newStatus, actorCustomerId, reason, ct);
        await SaveWithConcurrencyGuardAsync(ct);

        return (await GetByIdAsync(id, ct))!;
    }

    public async Task<OrderDetailDto> CancelByCustomerAsync(
        long id, long customerId, string? reason, CancellationToken ct = default)
    {
        var order = await _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new KeyNotFoundException($"Order {id} not found.");

        if (order.CustomerId != customerId)
            throw new UnauthorizedAccessException("You can only cancel your own orders.");
        if (order.Status != OrderStatus.Pending)
            throw new InvalidOperationException("Only pending orders can be cancelled.");

        await TransitionAsync(order, OrderStatus.Cancelled, customerId, reason ?? "Cancelled by customer", ct);
        await SaveWithConcurrencyGuardAsync(ct);

        return (await GetByIdAsync(id, ct))!;
    }

    // ---- internals ----

    /// Validate + apply a single state transition, restocking on cancellation and
    /// appending an audit event. Caller persists via SaveWithConcurrencyGuardAsync.
    private async Task TransitionAsync(
        Order order, OrderStatus newStatus, long? actorCustomerId, string? reason, CancellationToken ct)
    {
        if (order.Status == newStatus)
            throw new InvalidOperationException($"Order is already '{newStatus}'.");
        if (!OrderStatusTransitions.CanTransition(order.Status, newStatus))
            throw new InvalidOperationException(
                $"Cannot change order status from '{order.Status}' to '{newStatus}'.");

        var from = order.Status;

        if (newStatus == OrderStatus.Cancelled)
        {
            // Return reserved stock. Transition rules guarantee Cancelled is only
            // reachable from Pending/Confirmed, so this runs at most once per order.
            var restock = order.Items
                .GroupBy(i => i.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
            var ids = restock.Keys.ToArray();
            var products = await _db.Products.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
            foreach (var p in products)
                p.StockQuantity += restock[p.Id];

            // A cancelled-after-paid order is marked refunded (real gateway refund: Phase 2).
            if (order.PaymentStatus == PaymentStatus.Paid)
                order.PaymentStatus = PaymentStatus.Refunded;
        }

        order.Status = newStatus;
        order.UpdatedAt = DateTime.UtcNow;
        order.Events.Add(new OrderEvent
        {
            FromStatus = from,
            ToStatus = newStatus,
            ActorCustomerId = actorCustomerId,
            Reason = reason
        });
    }

    private async Task SaveWithConcurrencyGuardAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another order touched the same product's stock between read and save.
            // Surface as a 400 so the caller can retry rather than a 500.
            throw new InvalidOperationException(
                "Inventory changed while placing the order. Please try again.");
        }
    }

    private static OrderDetailDto MapDetail(Order order) => new(
        order.Id,
        order.OrderNumber,
        order.CustomerId,
        order.Customer?.FullName ?? string.Empty,
        order.Customer?.Email ?? string.Empty,
        order.OrderDate,
        order.Status,
        order.PaymentMethod,
        order.PaymentStatus,
        order.ShipFullName,
        order.ShipPhone,
        order.ShipAddress,
        order.Note,
        order.TotalAmount,
        order.Items.Select(i => new OrderItemDetailDto(
            i.ProductId, i.Product.Sku, i.Product.Name, i.Quantity, i.UnitPrice, i.LineTotal)).ToList(),
        order.Events.OrderBy(e => e.CreatedAt).Select(e => new OrderEventDto(
            e.FromStatus, e.ToStatus, e.ActorCustomerId, e.Reason, e.CreatedAt)).ToList(),
        OrderStatusTransitions.NextStates(order.Status).ToList());
}

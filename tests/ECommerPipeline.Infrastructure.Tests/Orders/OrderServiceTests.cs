using ECommerPipeline.Application.Orders.DTOs;
using ECommerPipeline.Domain.Entities;
using ECommerPipeline.Domain.Enums;
using ECommerPipeline.Infrastructure.Orders;
using ECommerPipeline.Infrastructure.Persistence.Oltp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ECommerPipeline.Infrastructure.Tests.Orders;

public class OrderServiceTests
{
    // Each test gets its own isolated in-memory database (unique name).
    private static OltpDbContext NewContext() =>
        new(new DbContextOptionsBuilder<OltpDbContext>()
            .UseInMemoryDatabase($"orders-{Guid.NewGuid()}")
            .Options);

    private static async Task SeedProductsAsync(OltpDbContext db)
    {
        db.Products.AddRange(
            new Product { Id = 1, Sku = "SKU-1", Name = "iPhone", Category = "Electronics", Price = 25_000_000m, StockQuantity = 100 },
            new Product { Id = 2, Sku = "SKU-2", Name = "Nike",   Category = "Footwear",    Price = 3_500_000m,  StockQuantity = 50 });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateAsync_persists_order_with_correct_total()
    {
        await using var db = NewContext();
        await SeedProductsAsync(db);
        var sut = new OrderService(db);

        var request = new CreateOrderRequest(
            CustomerId: 1,
            Items: new List<CreateOrderItem>
            {
                new(ProductId: 1, Quantity: 2),   // 2 × 25,000,000 = 50,000,000
                new(ProductId: 2, Quantity: 1),   // 1 × 3,500,000  =  3,500,000
            });

        var response = await sut.CreateAsync(request);

        // Total = 53,500,000
        response.TotalAmount.Should().Be(53_500_000m);
        response.OrderId.Should().BeGreaterThan(0);
        response.OrderNumber.Should().StartWith("ORD-");

        var saved = await db.Orders.Include(o => o.Items).SingleAsync();
        saved.Items.Should().HaveCount(2);
        saved.Status.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public async Task CreateAsync_computes_line_total_from_product_price_not_client_input()
    {
        await using var db = NewContext();
        await SeedProductsAsync(db);
        var sut = new OrderService(db);

        var request = new CreateOrderRequest(
            CustomerId: 1,
            Items: new List<CreateOrderItem> { new(ProductId: 1, Quantity: 3) });

        await sut.CreateAsync(request);

        var item = await db.OrderItems.SingleAsync();
        item.UnitPrice.Should().Be(25_000_000m);        // from DB, not client
        item.LineTotal.Should().Be(75_000_000m);        // 3 × price
    }

    [Fact]
    public async Task CreateAsync_throws_when_product_not_found()
    {
        await using var db = NewContext();
        await SeedProductsAsync(db);
        var sut = new OrderService(db);

        var request = new CreateOrderRequest(
            CustomerId: 1,
            Items: new List<CreateOrderItem> { new(ProductId: 999, Quantity: 1) });

        var act = () => sut.CreateAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*products not found*");
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_for_missing_order()
    {
        await using var db = NewContext();
        var sut = new OrderService(db);

        var result = await sut.GetByIdAsync(12345);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_writes_outbox_message()
    {
        await using var db = NewContext();
        await SeedProductsAsync(db);
        var sut = new OrderService(db);

        var created = await sut.CreateAsync(new CreateOrderRequest(
            CustomerId: 1, Items: new List<CreateOrderItem> { new(ProductId: 1, Quantity: 1) }));

        var outbox = await db.OutboxMessages.SingleAsync();
        outbox.EventType.Should().Be("OrderPlaced");
        outbox.OrderId.Should().Be(created.OrderId);
        outbox.ProcessedAt.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_decrements_stock()
    {
        await using var db = NewContext();
        await SeedProductsAsync(db);
        var sut = new OrderService(db);

        await sut.CreateAsync(new CreateOrderRequest(
            CustomerId: 1,
            Items: new List<CreateOrderItem> { new(ProductId: 1, Quantity: 3) }));

        var product = await db.Products.SingleAsync(p => p.Id == 1);
        product.StockQuantity.Should().Be(97);   // 100 - 3
    }

    [Fact]
    public async Task CreateAsync_rejects_when_insufficient_stock()
    {
        await using var db = NewContext();
        await SeedProductsAsync(db);
        var sut = new OrderService(db);

        var act = () => sut.CreateAsync(new CreateOrderRequest(
            CustomerId: 1,
            Items: new List<CreateOrderItem> { new(ProductId: 2, Quantity: 51) })); // only 50 in stock

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Insufficient stock*");

        // No order persisted and stock untouched.
        (await db.Orders.CountAsync()).Should().Be(0);
        (await db.Products.SingleAsync(p => p.Id == 2)).StockQuantity.Should().Be(50);
    }

    [Fact]
    public async Task UpdateStatusAsync_rejects_invalid_transition()
    {
        await using var db = NewContext();
        await SeedProductsAsync(db);
        var created = await new OrderService(db).CreateAsync(new CreateOrderRequest(
            CustomerId: 1, Items: new List<CreateOrderItem> { new(ProductId: 1, Quantity: 1) }));
        var sut = new OrderService(db);

        // Pending → Shipped is not allowed (must go through Confirmed first).
        var act = () => sut.UpdateStatusAsync(created.OrderId, OrderStatus.Shipped, actorCustomerId: 1, reason: null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot change order status*");
    }

    [Fact]
    public async Task CancelByCustomerAsync_restocks_and_marks_cancelled()
    {
        await using var db = NewContext();
        await SeedProductsAsync(db);
        db.Customers.Add(new Customer { Id = 1, FullName = "A", Email = "a@test.com" });
        await db.SaveChangesAsync();
        var created = await new OrderService(db).CreateAsync(new CreateOrderRequest(
            CustomerId: 1, Items: new List<CreateOrderItem> { new(ProductId: 1, Quantity: 4) }));
        var sut = new OrderService(db);

        var result = await sut.CancelByCustomerAsync(created.OrderId, customerId: 1, reason: "changed mind");

        result.Status.Should().Be(OrderStatus.Cancelled);
        (await db.Products.SingleAsync(p => p.Id == 1)).StockQuantity.Should().Be(100); // 96 + 4 back
        result.Events.Should().Contain(e => e.ToStatus == OrderStatus.Cancelled);
    }

    [Fact]
    public async Task CancelByCustomerAsync_rejects_non_owner()
    {
        await using var db = NewContext();
        await SeedProductsAsync(db);
        var created = await new OrderService(db).CreateAsync(new CreateOrderRequest(
            CustomerId: 1, Items: new List<CreateOrderItem> { new(ProductId: 1, Quantity: 1) }));
        var sut = new OrderService(db);

        var act = () => sut.CancelByCustomerAsync(created.OrderId, customerId: 999, reason: null);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task GetPagedAsync_filters_by_status()
    {
        await using var db = NewContext();
        await SeedProductsAsync(db);
        db.Customers.Add(new Customer { Id = 1, FullName = "A", Email = "a@test.com" });
        db.Orders.AddRange(
            new Order { Id = 1, OrderNumber = "ORD-1", CustomerId = 1, OrderDate = DateTime.UtcNow, Status = OrderStatus.Pending, TotalAmount = 100 },
            new Order { Id = 2, OrderNumber = "ORD-2", CustomerId = 1, OrderDate = DateTime.UtcNow, Status = OrderStatus.Delivered, TotalAmount = 200 });
        await db.SaveChangesAsync();
        var sut = new OrderService(db);

        var result = await sut.GetPagedAsync(new OrderQueryParams(Page: 1, PageSize: 20, Status: OrderStatus.Pending));

        result.Items.Should().HaveCount(1);
        result.Items[0].OrderNumber.Should().Be("ORD-1");
        result.Total.Should().Be(1);
    }
}

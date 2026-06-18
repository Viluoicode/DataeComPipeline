using ECommerPipeline.Application.Common.Interfaces;
using ECommerPipeline.Domain.Entities;
using ECommerPipeline.Domain.Enums;
using ECommerPipeline.Infrastructure.Notifications;
using ECommerPipeline.Infrastructure.Persistence.Oltp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ECommerPipeline.Infrastructure.Tests.Notifications;

public class OutboxDispatchJobTests
{
    private sealed class FakeEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = new();
        public bool Throw { get; set; }
        public Task SendAsync(EmailMessage m, CancellationToken ct = default)
        {
            if (Throw) throw new InvalidOperationException("smtp down");
            Sent.Add(m);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNotifier : ICustomerNotifier
    {
        public List<OrderNotification> Pushed { get; } = new();
        public Task NotifyOrderAsync(long customerId, OrderNotification n, CancellationToken ct = default)
        {
            Pushed.Add(n);
            return Task.CompletedTask;
        }
    }

    private static OltpDbContext NewContext() =>
        new(new DbContextOptionsBuilder<OltpDbContext>()
            .UseInMemoryDatabase($"outbox-{Guid.NewGuid()}").Options);

    private static async Task<OltpDbContext> SeedWithMessageAsync()
    {
        var db = NewContext();
        db.Customers.Add(new Customer { Id = 1, FullName = "Buyer", Email = "buyer@test.com" });
        var order = new Order
        {
            Id = 1, OrderNumber = "ORD-1", CustomerId = 1, OrderDate = DateTime.UtcNow,
            Status = OrderStatus.Pending, TotalAmount = 100,
        };
        db.Orders.Add(order);
        db.OutboxMessages.Add(new OutboxMessage { OrderId = 1, EventType = OutboxEventTypes.OrderPlaced });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task RunAsync_sends_email_pushes_notification_and_marks_processed()
    {
        await using var db = await SeedWithMessageAsync();
        var email = new FakeEmailSender();
        var notifier = new FakeNotifier();
        var sut = new OutboxDispatchJob(db, email, notifier, NullLogger<OutboxDispatchJob>.Instance);

        await sut.RunAsync();

        email.Sent.Should().ContainSingle().Which.ToEmail.Should().Be("buyer@test.com");
        notifier.Pushed.Should().ContainSingle().Which.OrderNumber.Should().Be("ORD-1");
        (await db.OutboxMessages.SingleAsync()).ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RunAsync_records_attempt_and_retries_on_failure()
    {
        await using var db = await SeedWithMessageAsync();
        var email = new FakeEmailSender { Throw = true };
        var sut = new OutboxDispatchJob(db, email, new FakeNotifier(), NullLogger<OutboxDispatchJob>.Instance);

        await sut.RunAsync();

        var msg = await db.OutboxMessages.SingleAsync();
        msg.ProcessedAt.Should().BeNull();        // not processed → will retry
        msg.Attempts.Should().Be(1);
        msg.LastError.Should().Contain("smtp down");
    }

    [Fact]
    public async Task RunAsync_is_noop_when_nothing_pending()
    {
        await using var db = NewContext();
        var email = new FakeEmailSender();
        var sut = new OutboxDispatchJob(db, email, new FakeNotifier(), NullLogger<OutboxDispatchJob>.Instance);

        await sut.RunAsync();

        email.Sent.Should().BeEmpty();
    }
}

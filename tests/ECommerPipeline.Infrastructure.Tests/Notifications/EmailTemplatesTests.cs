using ECommerPipeline.Domain.Entities;
using ECommerPipeline.Domain.Enums;
using ECommerPipeline.Infrastructure.Notifications;
using FluentAssertions;
using Xunit;

namespace ECommerPipeline.Infrastructure.Tests.Notifications;

public class EmailTemplatesTests
{
    private static Order Order(OrderStatus status = OrderStatus.Pending) => new()
    {
        OrderNumber = "ORD-TEST-9",
        TotalAmount = 250_000m,
        Status = status,
    };

    [Theory]
    [InlineData(OutboxEventTypes.OrderPlaced)]
    [InlineData(OutboxEventTypes.PaymentSucceeded)]
    [InlineData(OutboxEventTypes.OrderStatusChanged)]
    public void Build_includes_order_number_in_all_parts(string eventType)
    {
        var (subject, html, inApp) = EmailTemplates.Build(eventType, Order());

        subject.Should().Contain("ORD-TEST-9");
        html.Should().Contain("ORD-TEST-9");
        inApp.Should().Contain("ORD-TEST-9");
        html.Should().Contain("<"); // rendered HTML body
    }

    [Fact]
    public void PaymentSucceeded_mentions_payment_and_total()
    {
        var (subject, html, _) = EmailTemplates.Build(OutboxEventTypes.PaymentSucceeded, Order());

        subject.Should().Contain("Thanh toán");
        html.Should().Contain("250.000");   // vi-VN grouped amount
    }

    [Fact]
    public void OrderStatusChanged_shows_vietnamese_status()
    {
        var (subject, _, inApp) = EmailTemplates.Build(
            OutboxEventTypes.OrderStatusChanged, Order(OrderStatus.Shipped));

        subject.Should().Contain("Đang giao");
        inApp.Should().Contain("Đang giao");
    }

    [Fact]
    public void Unknown_event_falls_back_gracefully()
    {
        var (subject, html, inApp) = EmailTemplates.Build("SomethingElse", Order());

        subject.Should().NotBeNullOrWhiteSpace();
        html.Should().Contain("ORD-TEST-9");
        inApp.Should().NotBeNullOrWhiteSpace();
    }
}

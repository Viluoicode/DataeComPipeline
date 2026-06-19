using ECommerPipeline.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ECommerPipeline.Application.Tests.Orders;

public class OrderStatusTransitionsTests
{
    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Pending, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Delivered)]
    public void Valid_transitions_are_allowed(OrderStatus from, OrderStatus to)
    {
        OrderStatusTransitions.CanTransition(from, to).Should().BeTrue();
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Shipped)]    // must confirm first
    [InlineData(OrderStatus.Pending, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Delivered)] // must ship first
    [InlineData(OrderStatus.Shipped, OrderStatus.Cancelled)]   // too late to cancel
    [InlineData(OrderStatus.Delivered, OrderStatus.Shipped)]   // terminal
    [InlineData(OrderStatus.Delivered, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Pending)]   // terminal
    [InlineData(OrderStatus.Cancelled, OrderStatus.Confirmed)]
    public void Invalid_transitions_are_rejected(OrderStatus from, OrderStatus to)
    {
        OrderStatusTransitions.CanTransition(from, to).Should().BeFalse();
    }

    [Fact]
    public void Same_status_is_not_a_valid_transition()
    {
        OrderStatusTransitions.CanTransition(OrderStatus.Pending, OrderStatus.Pending).Should().BeFalse();
    }

    [Fact]
    public void NextStates_lists_reachable_states()
    {
        OrderStatusTransitions.NextStates(OrderStatus.Pending)
            .Should().BeEquivalentTo(new[] { OrderStatus.Confirmed, OrderStatus.Cancelled });

        OrderStatusTransitions.NextStates(OrderStatus.Delivered).Should().BeEmpty();
        OrderStatusTransitions.NextStates(OrderStatus.Cancelled).Should().BeEmpty();
    }
}

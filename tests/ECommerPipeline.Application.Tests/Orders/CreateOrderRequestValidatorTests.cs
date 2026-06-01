using ECommerPipeline.Application.Orders.DTOs;
using ECommerPipeline.Application.Orders.Validators;
using FluentAssertions;
using Xunit;

namespace ECommerPipeline.Application.Tests.Orders;

public class CreateOrderRequestValidatorTests
{
    private readonly CreateOrderRequestValidator _sut = new();

    private static CreateOrderRequest ValidRequest() =>
        new(CustomerId: 1, Items: new List<CreateOrderItem> { new(ProductId: 1, Quantity: 2) });

    [Fact]
    public void Valid_request_passes()
    {
        var result = _sut.Validate(ValidRequest());
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-999)]
    public void CustomerId_not_positive_fails(long customerId)
    {
        var req = ValidRequest() with { CustomerId = customerId };

        var result = _sut.Validate(req);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateOrderRequest.CustomerId));
    }

    [Fact]
    public void Empty_items_fails()
    {
        var req = ValidRequest() with { Items = new List<CreateOrderItem>() };

        var result = _sut.Validate(req);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("at least 1 item"));
    }

    [Fact]
    public void More_than_100_items_fails()
    {
        var items = Enumerable.Range(1, 101)
            .Select(i => new CreateOrderItem(ProductId: i, Quantity: 1))
            .ToList();
        var req = ValidRequest() with { Items = items };

        var result = _sut.Validate(req);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("more than 100 items"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Item_quantity_not_positive_fails(int quantity)
    {
        var req = ValidRequest() with
        {
            Items = new List<CreateOrderItem> { new(ProductId: 1, Quantity: quantity) }
        };

        var result = _sut.Validate(req);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Quantity must be greater than 0"));
    }

    [Fact]
    public void Item_quantity_over_10000_fails()
    {
        var req = ValidRequest() with
        {
            Items = new List<CreateOrderItem> { new(ProductId: 1, Quantity: 10_001) }
        };

        var result = _sut.Validate(req);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("cannot exceed 10,000"));
    }

    [Fact]
    public void Item_productId_not_positive_fails()
    {
        var req = ValidRequest() with
        {
            Items = new List<CreateOrderItem> { new(ProductId: 0, Quantity: 1) }
        };

        var result = _sut.Validate(req);

        result.IsValid.Should().BeFalse();
    }
}

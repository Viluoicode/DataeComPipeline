using ECommerPipeline.Application.Orders.DTOs;
using FluentValidation;

namespace ECommerPipeline.Application.Orders.Validators;

public class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.CustomerId)
            .GreaterThan(0).WithMessage("CustomerId must be greater than 0.");

        RuleFor(x => x.Items)
            .NotNull().WithMessage("Items is required.")
            .NotEmpty().WithMessage("Order must contain at least 1 item.")
            .Must(items => items.Count <= 100).WithMessage("An order cannot contain more than 100 items.");

        RuleForEach(x => x.Items).SetValidator(new CreateOrderItemValidator());

        // Shipping fields are optional at the API level (admin can create orders
        // without them); the storefront checkout enforces name + address itself.
        RuleFor(x => x.ShipFullName).MaximumLength(200);
        RuleFor(x => x.ShipPhone).MaximumLength(40);
        RuleFor(x => x.ShipAddress).MaximumLength(500);
        RuleFor(x => x.Note).MaximumLength(1000);
        RuleFor(x => x.PaymentMethod).IsInEnum();
    }
}

public class CreateOrderItemValidator : AbstractValidator<CreateOrderItem>
{
    public CreateOrderItemValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Quantity must be greater than 0.")
            .LessThanOrEqualTo(10_000).WithMessage("Quantity per line cannot exceed 10,000.");
    }
}

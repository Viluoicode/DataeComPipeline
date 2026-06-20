namespace ECommerPipeline.Infrastructure.Commerce;

/// Bound from the "Commerce" config section. Defaults to 0 (free shipping, no VAT)
/// so unit tests and zero-config dev keep the old "subtotal == total" behaviour.
public class CommerceOptions
{
    public const string SectionName = "Commerce";

    public decimal ShippingFee { get; set; } = 0m;
    /// Orders with subtotal at/above this get free shipping. 0 = rule disabled.
    public decimal FreeShippingThreshold { get; set; } = 0m;
    /// VAT rate applied to the subtotal, e.g. 0.08 for 8%. 0 = no VAT.
    public decimal VatRate { get; set; } = 0m;

    public decimal ShippingFor(decimal subtotal) =>
        FreeShippingThreshold > 0 && subtotal >= FreeShippingThreshold ? 0m : ShippingFee;

    public decimal TaxFor(decimal subtotal) =>
        VatRate <= 0 ? 0m : Math.Round(subtotal * VatRate, 0, MidpointRounding.AwayFromZero);
}

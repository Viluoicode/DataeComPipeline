using ECommerPipeline.Domain.Common;

namespace ECommerPipeline.Domain.Entities;

/// A saved shipping address in a customer's address book. The checkout can
/// pre-fill from these; one can be marked default.
public class CustomerAddress : BaseEntity
{
    public long CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public string FullName { get; set; } = null!;
    public string Phone { get; set; } = null!;
    public string Address { get; set; } = null!;
    public bool IsDefault { get; set; }
}

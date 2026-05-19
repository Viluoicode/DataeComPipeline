using ECommerPipeline.Domain.Common;

namespace ECommerPipeline.Domain.Entities;

public class Customer : BaseEntity
{
    public string FullName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? Phone { get; set; }
    public string? City { get; set; }

    public ICollection<Order> Orders { get; set; } = new List<Order>();
}

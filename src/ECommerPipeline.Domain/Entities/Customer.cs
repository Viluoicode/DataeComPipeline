using ECommerPipeline.Domain.Common;
using ECommerPipeline.Domain.Enums;

namespace ECommerPipeline.Domain.Entities;

public class Customer : BaseEntity
{
    public string FullName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? Phone { get; set; }
    public string? City { get; set; }

    // ---- Auth (nullable so seed-imported customers don't need passwords) ----
    public string? PasswordHash { get; set; }
    public UserRole Role { get; set; } = UserRole.Customer;
    public DateTime? LastLoginAt { get; set; }

    public ICollection<Order> Orders { get; set; } = new List<Order>();
}

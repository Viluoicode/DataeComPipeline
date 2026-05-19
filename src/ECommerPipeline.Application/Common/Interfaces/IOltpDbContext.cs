using ECommerPipeline.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ECommerPipeline.Application.Common.Interfaces;

public interface IOltpDbContext
{
    DbSet<Customer> Customers { get; }
    DbSet<Product> Products { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

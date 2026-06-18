using ECommerPipeline.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ECommerPipeline.Application.Common.Interfaces;

public interface IOltpDbContext
{
    DbSet<Customer> Customers { get; }
    DbSet<Product> Products { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<OrderEvent> OrderEvents { get; }
    DbSet<Payment> Payments { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<RefreshToken> RefreshTokens { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

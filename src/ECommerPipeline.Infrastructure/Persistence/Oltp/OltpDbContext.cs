using ECommerPipeline.Application.Common.Interfaces;
using ECommerPipeline.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ECommerPipeline.Infrastructure.Persistence.Oltp;

public class OltpDbContext : DbContext, IOltpDbContext
{
    public OltpDbContext(DbContextOptions<OltpDbContext> options) : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.ApplyConfigurationsFromAssembly(typeof(OltpDbContext).Assembly);
        base.OnModelCreating(b);
    }
}

using ECommerPipeline.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerPipeline.Infrastructure.Persistence.Oltp.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> e)
    {
        e.ToTable("Orders");
        e.HasKey(x => x.Id);
        e.Property(x => x.OrderNumber).HasMaxLength(40).IsRequired();
        e.Property(x => x.TotalAmount).HasColumnType("decimal(18,2)");
        e.Property(x => x.Status).HasConversion<int>();

        e.HasIndex(x => x.OrderNumber).IsUnique();
        e.HasIndex(x => x.OrderDate);
        e.HasIndex(x => new { x.CustomerId, x.OrderDate });

        e.HasOne(x => x.Customer)
            .WithMany(c => c.Orders)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        e.HasMany(x => x.Items)
            .WithOne(i => i.Order)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> e)
    {
        e.ToTable("OrderItems");
        e.HasKey(x => x.Id);
        e.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");
        e.Property(x => x.LineTotal).HasColumnType("decimal(18,2)");

        e.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        e.HasIndex(x => x.OrderId);
        e.HasIndex(x => x.ProductId);
    }
}

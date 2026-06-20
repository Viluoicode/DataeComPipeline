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
        e.Property(x => x.Subtotal).HasColumnType("decimal(18,2)");
        e.Property(x => x.ShippingFee).HasColumnType("decimal(18,2)");
        e.Property(x => x.TaxAmount).HasColumnType("decimal(18,2)");
        e.Property(x => x.TotalAmount).HasColumnType("decimal(18,2)");
        e.Property(x => x.Status).HasConversion<int>();
        e.Property(x => x.PaymentMethod).HasConversion<int>();
        e.Property(x => x.PaymentStatus).HasConversion<int>();

        e.Property(x => x.ShipFullName).HasMaxLength(200);
        e.Property(x => x.ShipPhone).HasMaxLength(40);
        e.Property(x => x.ShipAddress).HasMaxLength(500);
        e.Property(x => x.Note).HasMaxLength(1000);

        e.Property(x => x.RowVersion).IsRowVersion();

        e.HasIndex(x => x.OrderNumber).IsUnique();
        e.HasIndex(x => x.OrderDate);
        e.HasIndex(x => new { x.CustomerId, x.OrderDate });
        e.HasIndex(x => x.Status);

        e.HasOne(x => x.Customer)
            .WithMany(c => c.Orders)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        e.HasMany(x => x.Items)
            .WithOne(i => i.Order)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        e.HasMany(x => x.Events)
            .WithOne(ev => ev.Order)
            .HasForeignKey(ev => ev.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class OrderEventConfiguration : IEntityTypeConfiguration<OrderEvent>
{
    public void Configure(EntityTypeBuilder<OrderEvent> e)
    {
        e.ToTable("OrderEvents");
        e.HasKey(x => x.Id);
        e.Property(x => x.FromStatus).HasConversion<int>();
        e.Property(x => x.ToStatus).HasConversion<int>();
        e.Property(x => x.Reason).HasMaxLength(500);
        e.HasIndex(x => x.OrderId);
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

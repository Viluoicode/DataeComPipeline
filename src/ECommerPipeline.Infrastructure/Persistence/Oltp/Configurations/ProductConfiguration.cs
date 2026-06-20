using ECommerPipeline.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerPipeline.Infrastructure.Persistence.Oltp.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> e)
    {
        e.ToTable("Products");
        e.HasKey(x => x.Id);
        e.Property(x => x.Sku).HasMaxLength(50).IsRequired();
        e.Property(x => x.Name).HasMaxLength(300).IsRequired();
        e.Property(x => x.Category).HasMaxLength(100).IsRequired();
        e.Property(x => x.Brand).HasMaxLength(100);
        e.Property(x => x.Price).HasColumnType("decimal(18,2)");
        e.Property(x => x.ImageUrl).HasMaxLength(260);
        e.Property(x => x.RowVersion).IsRowVersion();
        e.HasIndex(x => x.Sku).IsUnique();
        e.HasIndex(x => x.Category);
    }
}

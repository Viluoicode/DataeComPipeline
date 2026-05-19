using ECommerPipeline.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerPipeline.Infrastructure.Persistence.Oltp.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> e)
    {
        e.ToTable("Customers");
        e.HasKey(x => x.Id);
        e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        e.Property(x => x.Email).HasMaxLength(200).IsRequired();
        e.Property(x => x.Phone).HasMaxLength(30);
        e.Property(x => x.City).HasMaxLength(100);
        e.HasIndex(x => x.Email).IsUnique();
    }
}

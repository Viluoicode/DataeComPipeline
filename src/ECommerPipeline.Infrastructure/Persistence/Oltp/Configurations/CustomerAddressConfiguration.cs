using ECommerPipeline.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerPipeline.Infrastructure.Persistence.Oltp.Configurations;

public class CustomerAddressConfiguration : IEntityTypeConfiguration<CustomerAddress>
{
    public void Configure(EntityTypeBuilder<CustomerAddress> e)
    {
        e.ToTable("CustomerAddresses");
        e.HasKey(x => x.Id);
        e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        e.Property(x => x.Phone).HasMaxLength(40).IsRequired();
        e.Property(x => x.Address).HasMaxLength(500).IsRequired();
        e.HasIndex(x => x.CustomerId);

        e.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using ECommerPipeline.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerPipeline.Infrastructure.Persistence.Oltp.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> e)
    {
        e.ToTable("Payments");
        e.HasKey(x => x.Id);
        e.Property(x => x.Provider).HasConversion<int>();
        e.Property(x => x.Status).HasConversion<int>();
        e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        e.Property(x => x.ProviderTxnRef).HasMaxLength(100).IsRequired();
        e.Property(x => x.GatewayTransactionId).HasMaxLength(100);
        e.Property(x => x.ResponseCode).HasMaxLength(50);
        e.Property(x => x.RawCallback).HasColumnType("nvarchar(max)");

        // Unique so a replayed/duplicated gateway callback maps to exactly one payment.
        e.HasIndex(x => x.ProviderTxnRef).IsUnique();
        e.HasIndex(x => x.OrderId);

        e.HasOne(x => x.Order)
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

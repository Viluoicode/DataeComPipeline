using ECommerPipeline.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerPipeline.Infrastructure.Persistence.Oltp.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> e)
    {
        e.ToTable("OutboxMessages");
        e.HasKey(x => x.Id);
        e.Property(x => x.EventType).HasMaxLength(100).IsRequired();
        e.Property(x => x.Payload).HasColumnType("nvarchar(max)");
        e.Property(x => x.LastError).HasMaxLength(2000);

        // Dispatcher polls unprocessed rows oldest-first.
        e.HasIndex(x => x.ProcessedAt);

        e.HasOne(x => x.Order)
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

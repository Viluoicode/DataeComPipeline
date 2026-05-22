using ECommerPipeline.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerPipeline.Infrastructure.Persistence.Oltp.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> e)
    {
        e.ToTable("RefreshTokens");
        e.HasKey(x => x.Id);
        e.Property(x => x.Token).HasMaxLength(200).IsRequired();
        e.Property(x => x.ReplacedByToken).HasMaxLength(200);
        e.Ignore(x => x.IsActive);  // computed, not stored

        e.HasIndex(x => x.Token).IsUnique();
        e.HasIndex(x => x.CustomerId);

        e.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

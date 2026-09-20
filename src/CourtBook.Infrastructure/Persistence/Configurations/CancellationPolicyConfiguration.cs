using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class CancellationPolicyConfiguration : IEntityTypeConfiguration<CancellationPolicy>
{
    public void Configure(EntityTypeBuilder<CancellationPolicy> builder)
    {
        builder.HasKey(cp => cp.Id);

        builder.Property(cp => cp.LateCancellationFeePercent)
            .HasColumnType("decimal(5,2)")
            .HasDefaultValue(50.0m);

        builder.Property(cp => cp.PolicyDescription)
            .HasMaxLength(500);

        builder.HasIndex(cp => cp.VenueId)
            .IsUnique();
    }
}

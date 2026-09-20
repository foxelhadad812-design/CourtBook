using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class PriceRuleConfiguration : IEntityTypeConfiguration<PriceRule>
{
    public void Configure(EntityTypeBuilder<PriceRule> builder)
    {
        builder.HasKey(pr => pr.Id);

        builder.Property(pr => pr.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(pr => pr.PriceMultiplier)
            .HasColumnType("decimal(4,2)")
            .HasDefaultValue(1.0m);

        builder.Property(pr => pr.FixedPrice)
            .HasColumnType("decimal(10,2)");

        builder.HasIndex(pr => new { pr.CourtId, pr.IsActive });
        builder.HasIndex(pr => new { pr.CourtId, pr.DayOfWeek, pr.IsActive });

        builder.HasOne(pr => pr.Court)
            .WithMany(c => c.PriceRules)
            .HasForeignKey(pr => pr.CourtId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

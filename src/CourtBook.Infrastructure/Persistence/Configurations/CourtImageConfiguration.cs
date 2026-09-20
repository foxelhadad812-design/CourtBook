using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class CourtImageConfiguration : IEntityTypeConfiguration<CourtImage>
{
    public void Configure(EntityTypeBuilder<CourtImage> builder)
    {
        builder.HasKey(ci => ci.Id);

        builder.Property(ci => ci.ImageUrl)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(ci => ci.Caption)
            .HasMaxLength(150);

        builder.HasIndex(ci => ci.CourtId);
        builder.HasIndex(ci => new { ci.CourtId, ci.IsPrimary });

        builder.HasOne(ci => ci.Court)
            .WithMany(c => c.Images)
            .HasForeignKey(ci => ci.CourtId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

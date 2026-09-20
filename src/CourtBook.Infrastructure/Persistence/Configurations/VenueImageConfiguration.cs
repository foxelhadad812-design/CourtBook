using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class VenueImageConfiguration : IEntityTypeConfiguration<VenueImage>
{
    public void Configure(EntityTypeBuilder<VenueImage> builder)
    {
        builder.HasKey(vi => vi.Id);

        builder.Property(vi => vi.ImageUrl)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(vi => vi.Caption)
            .HasMaxLength(150);

        builder.HasIndex(vi => vi.VenueId);
        builder.HasIndex(vi => new { vi.VenueId, vi.IsPrimary });

        builder.HasOne(vi => vi.Venue)
            .WithMany(v => v.Images)
            .HasForeignKey(vi => vi.VenueId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class VenueAmenityConfiguration : IEntityTypeConfiguration<VenueAmenity>
{
    public void Configure(EntityTypeBuilder<VenueAmenity> builder)
    {
        builder.HasKey(va => new { va.VenueId, va.AmenityId });

        builder.HasOne(va => va.Venue)
            .WithMany(v => v.Amenities)
            .HasForeignKey(va => va.VenueId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(va => va.Amenity)
            .WithMany(a => a.VenueAmenities)
            .HasForeignKey(va => va.AmenityId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

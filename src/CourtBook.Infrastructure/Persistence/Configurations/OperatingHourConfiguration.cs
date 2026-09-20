using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class OperatingHourConfiguration : IEntityTypeConfiguration<OperatingHour>
{
    public void Configure(EntityTypeBuilder<OperatingHour> builder)
    {
        builder.HasKey(oh => oh.Id);

        builder.HasIndex(oh => new { oh.VenueId, oh.DayOfWeek });

        builder.HasOne(oh => oh.Venue)
            .WithMany(v => v.OperatingHours)
            .HasForeignKey(oh => oh.VenueId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

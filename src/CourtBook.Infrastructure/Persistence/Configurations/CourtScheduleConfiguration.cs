using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class CourtScheduleConfiguration : IEntityTypeConfiguration<CourtSchedule>
{
    public void Configure(EntityTypeBuilder<CourtSchedule> builder)
    {
        builder.HasKey(cs => cs.Id);

        // A court can only have one schedule row per day of the week
        builder.HasIndex(cs => new { cs.CourtId, cs.DayOfWeek })
            .IsUnique();

        // DayOfWeek stored as int (0=Sunday … 6=Saturday) — the BCL default
        builder.Property(cs => cs.DayOfWeek)
            .IsRequired();

        builder.Property(cs => cs.OpenTime)
            .IsRequired();

        builder.Property(cs => cs.CloseTime)
            .IsRequired();

        // Cascade: if a court is deleted, remove its schedules
        builder.HasOne(cs => cs.Court)
            .WithMany(c => c.Schedules)
            .HasForeignKey(cs => cs.CourtId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

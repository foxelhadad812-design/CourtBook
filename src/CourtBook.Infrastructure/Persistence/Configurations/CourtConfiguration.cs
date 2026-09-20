using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class CourtConfiguration : IEntityTypeConfiguration<Court>
{
    public void Configure(EntityTypeBuilder<Court> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.Description)
            .HasMaxLength(1000);

        builder.Property(c => c.SurfaceType)
            .HasMaxLength(50);

        // Store SportType as a string for readability in the DB
        builder.Property(c => c.SportType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(c => c.PricePerHour)
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.Property(c => c.IsActive)
            .HasDefaultValue(true);

        // Performance indexes for booking searches and filters
        builder.HasIndex(c => c.VenueId);
        builder.HasIndex(c => new { c.VenueId, c.SportType });
        builder.HasIndex(c => new { c.SportType, c.IsActive });
        builder.HasIndex(c => c.PricePerHour);

        // A court belongs to one venue; cascade delete courts when a venue is deleted
        builder.HasOne(c => c.Venue)
            .WithMany(v => v.Courts)
            .HasForeignKey(c => c.VenueId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

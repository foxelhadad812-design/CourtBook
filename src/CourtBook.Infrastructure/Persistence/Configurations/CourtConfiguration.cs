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

        // Store SportType as a string for readability in the DB
        builder.Property(c => c.SportType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // Two decimal places are enough for pricing
        builder.Property(c => c.PricePerHour)
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.Property(c => c.IsActive)
            .HasDefaultValue(true);

        // A court belongs to one venue; cascade delete courts when a venue is deleted
        builder.HasOne(c => c.Venue)
            .WithMany(v => v.Courts)
            .HasForeignKey(c => c.VenueId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

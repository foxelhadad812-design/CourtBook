using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class VenueConfiguration : IEntityTypeConfiguration<Venue>
{
    public void Configure(EntityTypeBuilder<Venue> builder)
    {
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(v => v.Description)
            .HasMaxLength(2000);

        builder.Property(v => v.City)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(v => v.Area)
            .HasMaxLength(100);

        builder.Property(v => v.Address)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(v => v.Country)
            .HasMaxLength(50);

        builder.Property(v => v.Phone)
            .HasMaxLength(30);

        builder.Property(v => v.Email)
            .HasMaxLength(200);

        builder.Property(v => v.Website)
            .HasMaxLength(300);

        // Performance indexes for search, filtering, and discovery
        builder.HasIndex(v => v.City);
        builder.HasIndex(v => new { v.City, v.Area });
        builder.HasIndex(v => v.IsActive);
        builder.HasIndex(v => v.IsVerified);
        builder.HasIndex(v => v.AverageRating);
        builder.HasIndex(v => v.OwnerId);

        // A venue belongs to one owner (User)
        builder.HasOne(v => v.Owner)
            .WithMany(u => u.OwnedVenues)
            .HasForeignKey(v => v.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        // 1:1 CancellationPolicy
        builder.HasOne(v => v.CancellationPolicy)
            .WithOne(cp => cp.Venue)
            .HasForeignKey<CancellationPolicy>(cp => cp.VenueId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

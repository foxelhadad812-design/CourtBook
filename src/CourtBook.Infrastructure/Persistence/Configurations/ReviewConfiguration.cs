using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.HasKey(r => r.Id);

        // Crucial requirement: Only 1 review per completed booking!
        builder.HasIndex(r => r.BookingId)
            .IsUnique();

        builder.Property(r => r.Comment)
            .HasMaxLength(1500);

        builder.Property(r => r.OwnerResponse)
            .HasMaxLength(1500);

        // Performance indexes for venue rating breakdown and reviews feed
        builder.HasIndex(r => r.VenueId);
        builder.HasIndex(r => new { r.VenueId, r.OverallRating });
        builder.HasIndex(r => r.UserId);

        // Rating ranges 1 to 5 check constraints
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Review_OverallRating", "[OverallRating] >= 1 AND [OverallRating] <= 5");
            t.HasCheckConstraint("CK_Review_CourtQualityRating", "[CourtQualityRating] >= 1 AND [CourtQualityRating] <= 5");
            t.HasCheckConstraint("CK_Review_CleanlinessRating", "[CleanlinessRating] >= 1 AND [CleanlinessRating] <= 5");
            t.HasCheckConstraint("CK_Review_StaffRating", "[StaffRating] >= 1 AND [StaffRating] <= 5");
            t.HasCheckConstraint("CK_Review_ValueRating", "[ValueRating] >= 1 AND [ValueRating] <= 5");
        });

        // Venue relationship
        builder.HasOne(r => r.Venue)
            .WithMany(v => v.Reviews)
            .HasForeignKey(r => r.VenueId)
            .OnDelete(DeleteBehavior.Cascade);

        // User relationship
        builder.HasOne(r => r.User)
            .WithMany(u => u.Reviews)
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

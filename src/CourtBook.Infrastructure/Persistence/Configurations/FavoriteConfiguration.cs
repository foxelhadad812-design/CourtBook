using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class FavoriteConfiguration : IEntityTypeConfiguration<Favorite>
{
    public void Configure(EntityTypeBuilder<Favorite> builder)
    {
        builder.HasKey(f => f.Id);

        // Crucial requirement: Prevent duplicate favorites for same user and venue
        builder.HasIndex(f => new { f.UserId, f.VenueId })
            .IsUnique();

        builder.HasOne(f => f.User)
            .WithMany(u => u.Favorites)
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(f => f.Venue)
            .WithMany(v => v.Favorites)
            .HasForeignKey(f => f.VenueId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class GameConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> builder)
    {
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Title)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(g => g.SportType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(g => g.SkillLevel)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(g => g.AgeGroup)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(AgeGroup.AllAges);

        builder.Property(g => g.MinAge)
            .IsRequired(false);

        builder.Property(g => g.MaxAge)
            .IsRequired(false);

        builder.Property(g => g.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(g => g.PricePerPlayer)
            .HasColumnType("decimal(10,2)");

        builder.Property(g => g.Description)
            .HasMaxLength(1000);

        builder.Property(g => g.AccessCode)
            .HasMaxLength(32)
            .IsRequired(false);

        builder.Property(g => g.IsPrivate)
            .HasDefaultValue(false);

        builder.Property(g => g.HasTeams)
            .HasDefaultValue(true);

        builder.Property(g => g.ConcurrencyStamp)
            .IsConcurrencyToken()
            .IsRequired();

        // Discovery and conflict detection indexes
        builder.HasIndex(g => new { g.SportType, g.Date, g.Status });
        builder.HasIndex(g => new { g.VenueId, g.Date, g.Status });
        builder.HasIndex(g => new { g.CourtId, g.Date, g.Status });
        builder.HasIndex(g => new { g.IsPrivate, g.Status });
        builder.HasIndex(g => g.CreatorId);

        // Creator relationship
        builder.HasOne(g => g.Creator)
            .WithMany(u => u.CreatedGames)
            .HasForeignKey(g => g.CreatorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Venue relationship
        builder.HasOne(g => g.Venue)
            .WithMany(v => v.Games)
            .HasForeignKey(g => g.VenueId)
            .OnDelete(DeleteBehavior.Restrict);

        // Court relationship
        builder.HasOne(g => g.Court)
            .WithMany(c => c.Games)
            .HasForeignKey(g => g.CourtId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

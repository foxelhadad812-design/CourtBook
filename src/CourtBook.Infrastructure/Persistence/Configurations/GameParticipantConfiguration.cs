using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class GameParticipantConfiguration : IEntityTypeConfiguration<GameParticipant>
{
    public void Configure(EntityTypeBuilder<GameParticipant> builder)
    {
        builder.HasKey(gp => gp.Id);

        // Crucial requirement: Prevent duplicate joins to the same game
        builder.HasIndex(gp => new { gp.GameId, gp.UserId })
            .IsUnique();

        builder.Property(gp => gp.Team)
            .HasMaxLength(50)
            .IsRequired(false);

        builder.Property(gp => gp.IsReady)
            .HasDefaultValue(false);

        builder.Property(gp => gp.ConcurrencyStamp)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasOne(gp => gp.Game)
            .WithMany(g => g.Participants)
            .HasForeignKey(gp => gp.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(gp => gp.User)
            .WithMany(u => u.GameParticipations)
            .HasForeignKey(gp => gp.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

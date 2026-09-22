using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class GameInvitationConfiguration : IEntityTypeConfiguration<GameInvitation>
{
    public void Configure(EntityTypeBuilder<GameInvitation> builder)
    {
        builder.HasKey(gi => gi.Id);

        builder.Property(gi => gi.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(gi => gi.Message)
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(gi => gi.ConcurrencyStamp)
            .IsConcurrencyToken()
            .IsRequired();

        // Query performance indexes
        builder.HasIndex(gi => new { gi.GameId, gi.InviteeId, gi.Status });
        builder.HasIndex(gi => new { gi.InviteeId, gi.Status });
        builder.HasIndex(gi => new { gi.InviterId, gi.Status });

        // Relationships
        builder.HasOne(gi => gi.Game)
            .WithMany(g => g.Invitations)
            .HasForeignKey(gi => gi.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(gi => gi.Inviter)
            .WithMany(u => u.SentInvitations)
            .HasForeignKey(gi => gi.InviterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(gi => gi.Invitee)
            .WithMany(u => u.ReceivedInvitations)
            .HasForeignKey(gi => gi.InviteeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

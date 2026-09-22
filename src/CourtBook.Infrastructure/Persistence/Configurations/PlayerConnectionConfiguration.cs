using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class PlayerConnectionConfiguration : IEntityTypeConfiguration<PlayerConnection>
{
    public void Configure(EntityTypeBuilder<PlayerConnection> builder)
    {
        builder.HasKey(pc => pc.Id);

        builder.Property(pc => pc.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // Prevent duplicate relationships in the same direction
        builder.HasIndex(pc => new { pc.RequesterId, pc.AddresseeId })
            .IsUnique();

        // Query performance indexes
        builder.HasIndex(pc => new { pc.AddresseeId, pc.Status });
        builder.HasIndex(pc => new { pc.RequesterId, pc.Status });

        // Relationships
        builder.HasOne(pc => pc.Requester)
            .WithMany(u => u.SentConnections)
            .HasForeignKey(pc => pc.RequesterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(pc => pc.Addressee)
            .WithMany(u => u.ReceivedConnections)
            .HasForeignKey(pc => pc.AddresseeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

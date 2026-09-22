using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.TokenHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(r => r.TokenHash)
            .IsUnique()
            .HasDatabaseName("IX_RefreshTokens_TokenHash");

        builder.Property(r => r.FamilyId)
            .IsRequired();

        builder.HasIndex(r => r.FamilyId)
            .HasDatabaseName("IX_RefreshTokens_FamilyId");

        builder.HasIndex(r => new { r.UserId, r.ExpiresAt })
            .HasDatabaseName("IX_RefreshTokens_UserId_ExpiresAt");

        builder.Property(r => r.ReasonRevoked)
            .HasMaxLength(256);

        builder.Property(r => r.DeviceId)
            .HasMaxLength(128);

        builder.Property(r => r.DeviceName)
            .HasMaxLength(128);

        builder.Property(r => r.Platform)
            .HasMaxLength(64);

        builder.Property(r => r.AppVersion)
            .HasMaxLength(64);

        builder.Property(r => r.CreatedByIp)
            .HasMaxLength(64);

        // Relationships
        builder.HasOne(r => r.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.ReplacedByToken)
            .WithMany()
            .HasForeignKey(r => r.ReplacedByTokenId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

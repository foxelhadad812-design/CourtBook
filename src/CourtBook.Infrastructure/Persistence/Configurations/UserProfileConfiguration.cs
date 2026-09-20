using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Bio)
            .HasMaxLength(500);

        builder.Property(p => p.AvatarUrl)
            .HasMaxLength(500);

        builder.Property(p => p.SkillLevel)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(p => p.PreferredSport)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasIndex(p => p.UserId)
            .IsUnique();
    }
}

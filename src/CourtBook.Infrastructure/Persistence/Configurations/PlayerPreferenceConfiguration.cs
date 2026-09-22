using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class PlayerPreferenceConfiguration : IEntityTypeConfiguration<PlayerPreference>
{
    public void Configure(EntityTypeBuilder<PlayerPreference> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.PreferredCities)
            .HasMaxLength(500);

        builder.Property(p => p.PreferredSports)
            .HasMaxLength(500);

        builder.Property(p => p.PreferredDays)
            .HasMaxLength(200);

        builder.Property(p => p.PreferredTimeOfDay)
            .HasMaxLength(200);

        builder.Property(p => p.PreferredGameType)
            .HasMaxLength(200);

        builder.Property(p => p.PreferredSkillLevel)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired(false);

        builder.HasIndex(p => p.UserId)
            .IsUnique();
    }
}

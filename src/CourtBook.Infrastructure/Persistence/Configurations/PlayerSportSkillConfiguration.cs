using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class PlayerSportSkillConfiguration : IEntityTypeConfiguration<PlayerSportSkill>
{
    public void Configure(EntityTypeBuilder<PlayerSportSkill> builder)
    {
        builder.HasKey(s => s.Id);

        // One skill record per sport per user
        builder.HasIndex(s => new { s.UserId, s.SportType })
            .IsUnique();

        builder.Property(s => s.SportType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(s => s.SkillLevel)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(s => s.SkillScore)
            .IsRequired();

        builder.HasOne(s => s.User)
            .WithMany(u => u.SportSkills)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

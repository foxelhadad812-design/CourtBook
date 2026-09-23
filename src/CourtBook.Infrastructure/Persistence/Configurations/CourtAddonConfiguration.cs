using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class CourtAddonConfiguration : IEntityTypeConfiguration<CourtAddon>
{
    public void Configure(EntityTypeBuilder<CourtAddon> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.NameAr)
            .HasMaxLength(100);

        builder.Property(a => a.Price)
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.Property(a => a.Unit)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(a => a.IsAvailable)
            .HasDefaultValue(true)
            .IsRequired();

        builder.HasOne(a => a.Court)
            .WithMany(c => c.Addons)
            .HasForeignKey(a => a.CourtId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.CourtId);
    }
}

using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class PromoCodeConfiguration : IEntityTypeConfiguration<PromoCode>
{
    public void Configure(EntityTypeBuilder<PromoCode> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Code)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(p => p.Code)
            .IsUnique();

        builder.Property(p => p.Description)
            .HasMaxLength(250);

        builder.Property(p => p.DiscountType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.Value)
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.Property(p => p.MaxDiscountAmount)
            .HasColumnType("decimal(10,2)");

        builder.Property(p => p.MinBookingAmount)
            .HasColumnType("decimal(10,2)");

        builder.Property(p => p.UsageCount)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(p => p.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.HasMany(p => p.Usages)
            .WithOne(u => u.PromoCode)
            .HasForeignKey(u => u.PromoCodeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

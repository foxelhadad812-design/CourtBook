using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class PromoCodeUsageConfiguration : IEntityTypeConfiguration<PromoCodeUsage>
{
    public void Configure(EntityTypeBuilder<PromoCodeUsage> builder)
    {
        builder.HasKey(u => u.Id);

        builder.Property(u => u.DiscountAmount)
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.Property(u => u.UsedAt)
            .IsRequired();

        builder.HasOne(u => u.PromoCode)
            .WithMany(p => p.Usages)
            .HasForeignKey(u => u.PromoCodeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(u => u.User)
            .WithMany(usr => usr.PromoCodeUsages)
            .HasForeignKey(u => u.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(u => u.Booking)
            .WithMany()
            .HasForeignKey(u => u.BookingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(u => new { u.PromoCodeId, u.UserId });
    }
}

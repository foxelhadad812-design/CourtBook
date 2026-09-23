using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class BookingAddonConfiguration : IEntityTypeConfiguration<BookingAddon>
{
    public void Configure(EntityTypeBuilder<BookingAddon> builder)
    {
        builder.HasKey(ba => ba.Id);

        builder.Property(ba => ba.Quantity)
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(ba => ba.UnitPrice)
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.Property(ba => ba.TotalPrice)
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.HasOne(ba => ba.Booking)
            .WithMany(b => b.BookingAddons)
            .HasForeignKey(ba => ba.BookingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ba => ba.CourtAddon)
            .WithMany()
            .HasForeignKey(ba => ba.CourtAddonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(ba => new { ba.BookingId, ba.CourtAddonId });
    }
}

using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class RecoveryObligationConfiguration : IEntityTypeConfiguration<RecoveryObligation>
{
    public void Configure(EntityTypeBuilder<RecoveryObligation> builder)
    {
        builder.HasKey(o => o.Id);

        builder.Property(o => o.ObligationReference)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(o => o.TotalDeficitAmount)
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.Property(o => o.RemainingDeficitAmount)
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.Property(o => o.Currency)
            .HasMaxLength(10)
            .HasDefaultValue("EGP")
            .IsRequired();

        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(o => o.Reason)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(o => o.AdminNotes)
            .HasMaxLength(1000);

        builder.Property(o => o.CreatedAt)
            .IsRequired();

        builder.Property(o => o.ConcurrencyStamp)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasOne(o => o.Owner)
            .WithMany(u => u.RecoveryObligations)
            .HasForeignKey(o => o.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Booking)
            .WithMany()
            .HasForeignKey(o => o.BookingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Payment)
            .WithMany()
            .HasForeignKey(o => o.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(o => o.ObligationReference)
            .IsUnique();

        builder.HasIndex(o => o.OwnerId);
        builder.HasIndex(o => new { o.OwnerId, o.Status });
        builder.HasIndex(o => o.CreatedAt);
    }
}

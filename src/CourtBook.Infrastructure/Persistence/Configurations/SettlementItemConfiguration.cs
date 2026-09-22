using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class SettlementItemConfiguration : IEntityTypeConfiguration<SettlementItem>
{
    public void Configure(EntityTypeBuilder<SettlementItem> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.GrossAmount)
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.Property(s => s.CommissionAmount)
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.Property(s => s.NetAmount)
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.Property(s => s.SettledAt)
            .IsRequired();

        builder.HasOne(s => s.SettlementBatch)
            .WithMany(b => b.Items)
            .HasForeignKey(s => s.SettlementBatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Booking)
            .WithMany()
            .HasForeignKey(s => s.BookingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Payment)
            .WithMany()
            .HasForeignKey(s => s.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Owner)
            .WithMany()
            .HasForeignKey(s => s.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        // HARD INVARIANT: Exactly one SettlementItem per BookingId
        builder.HasIndex(s => s.BookingId)
            .IsUnique();

        builder.HasIndex(s => s.SettlementBatchId);
        builder.HasIndex(s => s.OwnerId);
        builder.HasIndex(s => s.SettledAt);
    }
}

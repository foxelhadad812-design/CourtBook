using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class TransactionLedgerConfiguration : IEntityTypeConfiguration<TransactionLedger>
{
    public void Configure(EntityTypeBuilder<TransactionLedger> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.GrossAmount)
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.Property(t => t.CommissionAmount)
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.Property(t => t.NetAmount)
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.Property(t => t.CommissionRateSnapshot)
            .HasColumnType("decimal(5,4)")
            .IsRequired();

        builder.Property(t => t.Currency)
            .HasMaxLength(10)
            .HasDefaultValue("EGP");

        builder.Property(t => t.EntryType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(t => t.Description)
            .HasMaxLength(500);

        builder.Property(t => t.ProviderReference)
            .HasMaxLength(200);

        // Relationship: ledger entries belong to a Payment
        builder.HasOne(t => t.Payment)
            .WithMany(p => p.LedgerEntries)
            .HasForeignKey(t => t.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.PaymentId);
        builder.HasIndex(t => t.BookingId);
        builder.HasIndex(t => t.OwnerId);
        builder.HasIndex(t => t.UserId);
        builder.HasIndex(t => t.CreatedAt);
        builder.HasIndex(t => t.EntryType);
    }
}

using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Amount)
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.Property(p => p.CommissionAmount)
            .HasColumnType("decimal(10,2)")
            .HasDefaultValue(0m);

        builder.Property(p => p.OwnerNetAmount)
            .HasColumnType("decimal(10,2)")
            .HasDefaultValue(0m);

        builder.Property(p => p.Currency)
            .HasMaxLength(10)
            .HasDefaultValue("EGP");

        builder.Property(p => p.Method)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(p => p.TransactionReference)
            .HasMaxLength(200);

        builder.Property(p => p.ProviderOrderId)
            .HasMaxLength(200);

        builder.Property(p => p.PaymentUrl)
            .HasMaxLength(2000);

        builder.HasIndex(p => p.BookingId)
            .IsUnique();

        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => p.ProviderOrderId);
    }
}

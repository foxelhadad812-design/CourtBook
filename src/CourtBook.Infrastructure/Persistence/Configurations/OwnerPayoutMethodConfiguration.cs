using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class OwnerPayoutMethodConfiguration : IEntityTypeConfiguration<OwnerPayoutMethod>
{
    public void Configure(EntityTypeBuilder<OwnerPayoutMethod> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Type)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(m => m.AccountHolderName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(m => m.BankName)
            .HasMaxLength(100);

        builder.Property(m => m.Iban)
            .HasMaxLength(50);

        builder.Property(m => m.AccountNumber)
            .HasMaxLength(50);

        builder.Property(m => m.InstaPayAddress)
            .HasMaxLength(100);

        builder.Property(m => m.MobileWalletNumber)
            .HasMaxLength(20);

        builder.Property(m => m.IsDefault)
            .HasDefaultValue(false);

        builder.Property(m => m.IsActive)
            .HasDefaultValue(true);

        builder.Property(m => m.IsVerified)
            .HasDefaultValue(false);

        builder.Property(m => m.ConcurrencyStamp)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasOne(m => m.Owner)
            .WithMany(u => u.PayoutMethods)
            .HasForeignKey(m => m.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => m.OwnerId);
        builder.HasIndex(m => new { m.OwnerId, m.IsDefault });
        builder.HasIndex(m => new { m.OwnerId, m.IsActive });
    }
}

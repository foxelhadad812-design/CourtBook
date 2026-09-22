using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class OwnerBalanceConfiguration : IEntityTypeConfiguration<OwnerBalance>
{
    public void Configure(EntityTypeBuilder<OwnerBalance> builder)
    {
        builder.HasKey(b => b.OwnerId);

        builder.Property(b => b.PendingBalance)
            .HasColumnType("decimal(10,2)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(b => b.AvailableBalance)
            .HasColumnType("decimal(10,2)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(b => b.InFlightBalance)
            .HasColumnType("decimal(10,2)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(b => b.TotalPaidOut)
            .HasColumnType("decimal(10,2)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(b => b.TotalRefunded)
            .HasColumnType("decimal(10,2)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(b => b.OutstandingDeficit)
            .HasColumnType("decimal(10,2)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(b => b.Currency)
            .HasMaxLength(10)
            .HasDefaultValue("EGP")
            .IsRequired();

        builder.Property(b => b.ConcurrencyStamp)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(b => b.UpdatedAt)
            .IsRequired();

        builder.HasOne(b => b.Owner)
            .WithOne(u => u.OwnerBalance)
            .HasForeignKey<OwnerBalance>(b => b.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

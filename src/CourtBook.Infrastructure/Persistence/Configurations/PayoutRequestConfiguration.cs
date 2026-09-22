using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class PayoutRequestConfiguration : IEntityTypeConfiguration<PayoutRequest>
{
    public void Configure(EntityTypeBuilder<PayoutRequest> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.PayoutReference)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.Amount)
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.Property(r => r.Fee)
            .HasColumnType("decimal(10,2)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(r => r.NetAmount)
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.Property(r => r.Currency)
            .HasMaxLength(10)
            .HasDefaultValue("EGP")
            .IsRequired();

        builder.Property(r => r.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(r => r.RejectionReason)
            .HasMaxLength(500);

        builder.Property(r => r.ExternalTransactionReference)
            .HasMaxLength(200);

        builder.Property(r => r.DisbursementNote)
            .HasMaxLength(500);

        builder.Property(r => r.ConcurrencyStamp)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasOne(r => r.Owner)
            .WithMany(u => u.PayoutRequests)
            .HasForeignKey(r => r.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.PayoutMethod)
            .WithMany(m => m.PayoutRequests)
            .HasForeignKey(r => r.PayoutMethodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ApprovedByAdmin)
            .WithMany()
            .HasForeignKey(r => r.ApprovedByAdminId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.DisbursedByAdmin)
            .WithMany()
            .HasForeignKey(r => r.DisbursedByAdminId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.PayoutReference)
            .IsUnique();

        builder.HasIndex(r => r.OwnerId);
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.SubmittedAt);

        builder.HasIndex(r => r.ExternalTransactionReference)
            .IsUnique()
            .HasFilter("[ExternalTransactionReference] IS NOT NULL");
    }
}

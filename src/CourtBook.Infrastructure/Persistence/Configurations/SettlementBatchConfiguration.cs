using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class SettlementBatchConfiguration : IEntityTypeConfiguration<SettlementBatch>
{
    public void Configure(EntityTypeBuilder<SettlementBatch> builder)
    {
        builder.HasKey(b => b.Id);

        builder.Property(b => b.BatchReference)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(b => b.PeriodStart)
            .IsRequired();

        builder.Property(b => b.PeriodEnd)
            .IsRequired();

        builder.Property(b => b.TotalGross)
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.Property(b => b.TotalCommission)
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.Property(b => b.TotalNet)
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.Property(b => b.ItemCount)
            .IsRequired();

        builder.Property(b => b.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(b => b.CreatedAt)
            .IsRequired();

        builder.Property(b => b.CreatedBy)
            .IsRequired();

        builder.HasIndex(b => b.BatchReference)
            .IsUnique();

        builder.HasIndex(b => b.CreatedAt);
    }
}

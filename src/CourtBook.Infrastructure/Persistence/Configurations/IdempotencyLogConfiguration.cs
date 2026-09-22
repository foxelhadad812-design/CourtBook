using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class IdempotencyLogConfiguration : IEntityTypeConfiguration<IdempotencyLog>
{
    public void Configure(EntityTypeBuilder<IdempotencyLog> builder)
    {
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Provider)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(i => i.ProviderTransactionId)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(i => i.Action)
            .HasMaxLength(100)
            .IsRequired();

        // Unique constraint — prevents duplicate webhook processing
        builder.HasIndex(i => new { i.Provider, i.ProviderTransactionId })
            .IsUnique()
            .HasDatabaseName("IX_IdempotencyLog_Provider_TransactionId");

        builder.HasIndex(i => i.PaymentId);
        builder.HasIndex(i => i.ProcessedAt);
    }
}

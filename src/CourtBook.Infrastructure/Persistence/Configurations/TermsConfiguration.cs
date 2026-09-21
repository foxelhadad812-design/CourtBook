using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class TermsDocumentConfiguration : IEntityTypeConfiguration<TermsDocument>
{
    public void Configure(EntityTypeBuilder<TermsDocument> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Type)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(t => t.Version)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(t => t.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.Content)
            .IsRequired();

        builder.HasIndex(t => new { t.Type, t.IsActive });
    }
}

public class TermsAcceptanceConfiguration : IEntityTypeConfiguration<TermsAcceptance>
{
    public void Configure(EntityTypeBuilder<TermsAcceptance> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.IpAddress)
            .HasMaxLength(50);

        builder.HasIndex(a => a.UserId);
        builder.HasIndex(a => a.TermsDocumentId);

        builder.HasOne(a => a.User)
            .WithMany(u => u.TermsAcceptances)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.TermsDocument)
            .WithMany(t => t.Acceptances)
            .HasForeignKey(a => a.TermsDocumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

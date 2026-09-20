using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class VenueConfiguration : IEntityTypeConfiguration<Venue>
{
    public void Configure(EntityTypeBuilder<Venue> builder)
    {
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(v => v.City)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(v => v.Address)
            .IsRequired()
            .HasMaxLength(300);

        // A venue belongs to one owner (User); deleting an owner is restricted
        // while they still have venues — ownership must be transferred first.
        builder.HasOne(v => v.Owner)
            .WithMany(u => u.OwnedVenues)
            .HasForeignKey(v => v.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

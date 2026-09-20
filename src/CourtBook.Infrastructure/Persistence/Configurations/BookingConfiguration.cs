using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.HasKey(b => b.Id);

        // Store BookingStatus as a string for readability in the DB
        builder.Property(b => b.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(b => b.TotalPrice)
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.Property(b => b.StartTime)
            .IsRequired();

        builder.Property(b => b.EndTime)
            .IsRequired();

        builder.Property(b => b.CreatedAt)
            .IsRequired();

        // DB-level guard: a booking must end after it starts
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Booking_EndTime_After_StartTime",
            "[EndTime] > [StartTime]"));

        // TODO (Phase 2): Add a DB-level filtered index or trigger to prevent
        // overlapping bookings for the same court. EF Core Fluent API cannot
        // express this natively; it will be added as raw SQL in a dedicated migration.

        // Court → Bookings: restrict deletion if active bookings exist
        builder.HasOne(b => b.Court)
            .WithMany(c => c.Bookings)
            .HasForeignKey(b => b.CourtId)
            .OnDelete(DeleteBehavior.Restrict);

        // User → Bookings: restrict deletion if the user still has bookings
        builder.HasOne(b => b.User)
            .WithMany(u => u.Bookings)
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

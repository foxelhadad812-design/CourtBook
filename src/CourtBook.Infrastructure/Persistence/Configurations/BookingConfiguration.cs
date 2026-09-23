using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourtBook.Infrastructure.Persistence.Configurations;

public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.HasKey(b => b.Id);

        builder.Property(b => b.BookingReference)
            .IsRequired()
            .HasMaxLength(30);

        builder.HasIndex(b => b.BookingReference)
            .IsUnique();

        builder.Property(b => b.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(b => b.PaymentStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(b => b.TotalPrice)
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.Property(b => b.CancellationFee)
            .HasColumnType("decimal(10,2)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(b => b.StartTime)
            .IsRequired();

        builder.Property(b => b.EndTime)
            .IsRequired();

        builder.Property(b => b.CreatedAt)
            .IsRequired();

        builder.Property(b => b.Notes)
            .HasMaxLength(500);

        builder.Property(b => b.CancellationReason)
            .HasMaxLength(500);

        // DB-level guard: a booking must end after it starts
        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "CK_Booking_EndTime_After_StartTime",
                "[EndTime] > [StartTime]");

            // The Bookings table has an INSTEAD OF INSERT/UPDATE trigger
            // (TRG_Booking_NoOverlap) that performs the actual DML.
            // SQL Server OUTPUT clause is incompatible with tables that have
            // triggers, so EF Core must not emit OUTPUT for Booking DML.
            // This is required because EF Core SQL Server provider defaults to
            // UseSqlOutputClause(true) for store-generated properties, and the
            // model snapshot inherits this via UseIdentityColumns at the model
            // level (AppDbContextModelSnapshot line 23).
            t.UseSqlOutputClause(false);
        });

        // Critical index for availability and double-booking conflict detection
        builder.HasIndex(b => new { b.CourtId, b.StartTime, b.EndTime, b.Status });

        // Index for user booking history
        builder.HasIndex(b => new { b.UserId, b.StartTime });

        // Court → Bookings
        builder.HasOne(b => b.Court)
            .WithMany(c => c.Bookings)
            .HasForeignKey(b => b.CourtId)
            .OnDelete(DeleteBehavior.Restrict);

        // User → Bookings
        builder.HasOne(b => b.User)
            .WithMany(u => u.Bookings)
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // 1:1 Payment
        builder.HasOne(b => b.Payment)
            .WithOne(p => p.Booking)
            .HasForeignKey<Payment>(p => p.BookingId)
            .OnDelete(DeleteBehavior.Cascade);

        // 1:1 Review
        builder.HasOne(b => b.Review)
            .WithOne(r => r.Booking)
            .HasForeignKey<Review>(r => r.BookingId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

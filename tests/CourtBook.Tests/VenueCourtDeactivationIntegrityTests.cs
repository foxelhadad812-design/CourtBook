using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CourtBook.Tests;

public class VenueCourtDeactivationIntegrityTests
{
    [Fact]
    public async Task DeleteCourt_RejectsWithException_WhenUpcomingConfirmedBookingsExist()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var courtService = new CourtService(db);

        // Add upcoming confirmed booking
        db.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-UPCOMING-01",
            CourtId = courtId,
            UserId = clientId,
            StartTime = DateTime.UtcNow.AddDays(2),
            EndTime = DateTime.UtcNow.AddDays(2).AddHours(1),
            Status = BookingStatus.Confirmed,
            TotalPrice = 200m
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => courtService.DeleteAsync(ownerId, "Owner", venueId, courtId));

        Assert.Contains("active upcoming bookings", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Verify court still active and present
        var court = await db.Courts.FindAsync(courtId);
        Assert.NotNull(court);
        Assert.True(court.IsActive);
    }

    [Fact]
    public async Task DeleteCourt_SoftDeactivates_WhenPastHistoricalBookingsExist()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var courtService = new CourtService(db);

        // Add past completed booking
        db.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-PAST-01",
            CourtId = courtId,
            UserId = clientId,
            StartTime = DateTime.UtcNow.AddDays(-5),
            EndTime = DateTime.UtcNow.AddDays(-5).AddHours(1),
            Status = BookingStatus.Completed,
            TotalPrice = 200m
        });
        await db.SaveChangesAsync();

        var result = await courtService.DeleteAsync(ownerId, "Owner", venueId, courtId);

        Assert.True(result);

        // Verify court is soft-deactivated, NOT removed from database
        var court = await db.Courts.FindAsync(courtId);
        Assert.NotNull(court);
        Assert.False(court.IsActive);

        // Verify historical booking intact
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.CourtId == courtId);
        Assert.NotNull(booking);
    }

    [Fact]
    public async Task DeleteCourt_HardDeletes_WhenZeroBookingsOrGamesEverExisted()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, _, ownerId, _) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var courtService = new CourtService(db);

        // Add brand new court with zero bookings
        var newCourt = new Court
        {
            Id = Guid.NewGuid(),
            VenueId = venueId,
            Name = "Pristine Court",
            SportType = SportType.Tennis,
            PricePerHour = 150m,
            IsActive = true
        };
        db.Courts.Add(newCourt);
        await db.SaveChangesAsync();

        var result = await courtService.DeleteAsync(ownerId, "Owner", venueId, newCourt.Id);

        Assert.True(result);

        // Verify court is physically removed
        var court = await db.Courts.FindAsync(newCourt.Id);
        Assert.Null(court);
    }

    [Fact]
    public async Task DeleteVenue_RejectsWithException_WhenUpcomingConfirmedBookingsExist()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var venueService = new VenueService(db);

        db.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-VENUE-UPCOMING",
            CourtId = courtId,
            UserId = clientId,
            StartTime = DateTime.UtcNow.AddDays(3),
            EndTime = DateTime.UtcNow.AddDays(3).AddHours(1),
            Status = BookingStatus.Confirmed,
            TotalPrice = 200m
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => venueService.DeleteAsync(ownerId, "Owner", venueId));

        Assert.Contains("active upcoming bookings", ex.Message, StringComparison.OrdinalIgnoreCase);

        var venue = await db.Venues.FindAsync(venueId);
        Assert.NotNull(venue);
        Assert.True(venue.IsActive);
    }

    [Fact]
    public async Task DeleteVenue_SoftDeactivates_WhenHistoricalBookingsExist()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var venueService = new VenueService(db);

        db.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-VENUE-PAST",
            CourtId = courtId,
            UserId = clientId,
            StartTime = DateTime.UtcNow.AddDays(-10),
            EndTime = DateTime.UtcNow.AddDays(-10).AddHours(1),
            Status = BookingStatus.Completed,
            TotalPrice = 200m
        });
        await db.SaveChangesAsync();

        var result = await venueService.DeleteAsync(ownerId, "Owner", venueId);

        Assert.True(result);

        // Verify venue and its courts are deactivated
        var venue = await db.Venues.Include(v => v.Courts).FirstOrDefaultAsync(v => v.Id == venueId);
        Assert.NotNull(venue);
        Assert.False(venue.IsActive);
        Assert.All(venue.Courts, c => Assert.False(c.IsActive));
    }
}

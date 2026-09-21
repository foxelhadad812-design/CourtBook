using CourtBook.Application.DTOs;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Services;
using Xunit;

namespace CourtBook.Tests;

public class OwnerDashboardAndAuthorizationTests
{
    [Fact]
    public async Task Owner_CanAccessOwnDashboard_AndStatsAreAccurate()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var ownerService = new OwnerService(db);

        // Add 1 upcoming confirmed booking
        var now = DateTime.UtcNow;
        var futureDate = DateOnly.FromDateTime(now.AddDays(2));
        var futureStart = DateTime.SpecifyKind(futureDate.ToDateTime(new TimeOnly(18, 0)), DateTimeKind.Utc);

        db.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-OWNER-01",
            CourtId = courtId,
            UserId = clientId,
            StartTime = futureStart,
            EndTime = futureStart.AddHours(1),
            Status = BookingStatus.Confirmed,
            TotalPrice = 250m
        });

        // Add 1 completed booking
        var pastDate = DateOnly.FromDateTime(now.AddDays(-2));
        var pastStart = DateTime.SpecifyKind(pastDate.ToDateTime(new TimeOnly(14, 0)), DateTimeKind.Utc);
        db.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-OWNER-02",
            CourtId = courtId,
            UserId = clientId,
            StartTime = pastStart,
            EndTime = pastStart.AddHours(1),
            Status = BookingStatus.Completed,
            TotalPrice = 200m
        });
        await db.SaveChangesAsync();

        // Query Dashboard
        var summary = await ownerService.GetDashboardSummaryAsync(ownerId);

        Assert.NotNull(summary);
        Assert.Equal(1, summary.TotalVenues);
        Assert.Equal(1, summary.TotalCourts);
        Assert.Equal(1, summary.UpcomingBookingsCount);
        Assert.Equal(1, summary.CompletedBookingsCount);
        Assert.Equal(0, summary.CancelledBookingsCount);
        Assert.Equal(450m, summary.TotalRevenue); // 250 + 200 = 450
        Assert.Equal("Football Court 1", summary.MostBookedCourtName);
        Assert.Equal(2, summary.MostBookedCourtCount);
        Assert.Single(summary.Venues);
        Assert.Equal("Stars Arena", summary.Venues[0].Name);
    }

    [Fact]
    public async Task CrossOwner_OwnerACannotAccess_OwnerB_VenueOrBooking()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueAId, courtAId, ownerAId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        // Create Owner B with Venue B and Court B
        var ownerB = new User
        {
            Id = Guid.NewGuid(),
            Name = "Owner B (Tarek)",
            Email = "ownerb@test.com",
            PasswordHash = "hash",
            Role = Role.Owner
        };
        var venueB = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerB.Id,
            Name = "Arena B",
            City = "Giza",
            Area = "Dokki",
            Address = "Dokki St",
            IsActive = true
        };
        var courtB = new Court
        {
            Id = Guid.NewGuid(),
            VenueId = venueB.Id,
            Name = "Padel Court B",
            SportType = SportType.Padel,
            PricePerHour = 300m,
            IsActive = true
        };
        var bookingB = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-VENUE-B-01",
            CourtId = courtB.Id,
            UserId = clientId,
            StartTime = DateTime.UtcNow.AddDays(1),
            EndTime = DateTime.UtcNow.AddDays(1).AddHours(1),
            Status = BookingStatus.Confirmed,
            TotalPrice = 300m
        };

        db.Users.Add(ownerB);
        db.Venues.Add(venueB);
        db.Courts.Add(courtB);
        db.Bookings.Add(bookingB);
        await db.SaveChangesAsync();

        var ownerService = new OwnerService(db);

        // 1. Owner A queries venues -> only Venue A returned, Venue B NEVER returned
        var ownerAVenues = await ownerService.GetOwnerVenuesAsync(ownerAId);
        Assert.Single(ownerAVenues);
        Assert.Equal(venueAId, ownerAVenues[0].Id);
        Assert.DoesNotContain(ownerAVenues, v => v.Id == venueB.Id);

        // 2. Owner A queries dashboard summary -> only includes Owner A data
        var ownerASummary = await ownerService.GetDashboardSummaryAsync(ownerAId);
        Assert.Equal(1, ownerASummary.TotalVenues);
        Assert.Equal(0m, ownerASummary.TotalRevenue); // No bookings on Venue A

        // 3. Owner A attempts to directly access Venue B by ID -> UnauthorizedAccessException
        var exVenue = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ownerService.GetOwnerVenueByIdAsync(ownerAId, venueB.Id));
        Assert.Contains("permission", exVenue.Message, StringComparison.OrdinalIgnoreCase);

        // 4. Owner A attempts to directly access Booking B by ID -> UnauthorizedAccessException
        var exBooking = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ownerService.GetOwnerBookingByIdAsync(ownerAId, bookingB.Id));
        Assert.Contains("permission", exBooking.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OwnerRevenue_AccuratelyAccountsFor_ConfirmedAndCancelledBookings()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        // Cancellation policy on venue: 24h free, 50% late fee
        db.CancellationPolicies.Add(new CancellationPolicy
        {
            Id = Guid.NewGuid(),
            VenueId = venueId,
            FreeCancellationHours = 24,
            LateCancellationFeePercent = 50m
        });
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;

        // 1. Confirmed booking -> 200m revenue
        db.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-REV-01",
            CourtId = courtId,
            UserId = clientId,
            StartTime = now.AddDays(3),
            EndTime = now.AddDays(3).AddHours(1),
            Status = BookingStatus.Confirmed,
            TotalPrice = 200m
        });

        // 2. Free cancelled booking (>24h before match) -> 0m revenue (100% refund)
        db.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-REV-FREE",
            CourtId = courtId,
            UserId = clientId,
            StartTime = now.AddDays(5),
            EndTime = now.AddDays(5).AddHours(1),
            Status = BookingStatus.Cancelled,
            TotalPrice = 300m,
            CancelledAt = now // cancelled 5 days ahead, well within free window
        });

        // 3. Late cancelled booking (<24h before match, e.g. cancelled 6h ahead) -> 50% fee retained = 100m revenue
        db.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-REV-LATE",
            CourtId = courtId,
            UserId = clientId,
            StartTime = now.AddHours(10),
            EndTime = now.AddHours(11),
            Status = BookingStatus.Cancelled,
            TotalPrice = 200m,
            CancelledAt = now.AddHours(4) // cancelled 6h before match -> 50% late fee = 100m
        });

        await db.SaveChangesAsync();

        var ownerService = new OwnerService(db);
        var summary = await ownerService.GetDashboardSummaryAsync(ownerId);

        // Total revenue = 200 (confirmed) + 0 (free cancelled) + 100 (late fee retained) = 300m
        Assert.Equal(300m, summary.TotalRevenue);
        Assert.Equal(1, summary.UpcomingBookingsCount);
        Assert.Equal(2, summary.CancelledBookingsCount);
    }

    [Fact]
    public async Task OwnerBookings_SupportsPagedFiltering_ByVenueAndStatus()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var now = DateTime.UtcNow;
        for (int i = 1; i <= 5; i++)
        {
            db.Bookings.Add(new Booking
            {
                Id = Guid.NewGuid(),
                BookingReference = $"PS-PAGED-{i:00}",
                CourtId = courtId,
                UserId = clientId,
                StartTime = now.AddDays(i),
                EndTime = now.AddDays(i).AddHours(1),
                Status = BookingStatus.Confirmed,
                TotalPrice = 200m
            });
        }
        await db.SaveChangesAsync();

        var ownerService = new OwnerService(db);

        // Page 1, PageSize 2
        var paged = await ownerService.GetOwnerBookingsAsync(ownerId, new OwnerBookingQueryRequest
        {
            Page = 1,
            PageSize = 2,
            Status = "upcoming"
        });

        Assert.Equal(5, paged.TotalCount);
        Assert.Equal(2, paged.Items.Count);
        Assert.Equal(3, paged.TotalPages);
        Assert.True(paged.HasNextPage);
        Assert.False(paged.HasPreviousPage);
    }
}

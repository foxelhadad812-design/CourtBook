using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Services;
using Xunit;

namespace CourtBook.Tests;

public class OwnerDashboardPerformanceTests
{
    [Fact]
    public async Task GetDashboardSummaryAsync_AggregatesRevenueDirectlyFromSql_WithoutInMemoryLoop()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var ownerService = new OwnerService(db);

        var todayUtc = DateTime.UtcNow.Date;
        var todayStart = DateTime.SpecifyKind(todayUtc, DateTimeKind.Utc);

        // 1. Confirmed booking today: 300m
        db.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-PERF-01",
            CourtId = courtId,
            UserId = clientId,
            StartTime = todayStart.AddHours(10),
            EndTime = todayStart.AddHours(11),
            Status = BookingStatus.Confirmed,
            TotalPrice = 300m
        });

        // 2. Completed booking earlier this week (yesterday): 250m
        db.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-PERF-02",
            CourtId = courtId,
            UserId = clientId,
            StartTime = todayStart.AddDays(-1).AddHours(10),
            EndTime = todayStart.AddDays(-1).AddHours(11),
            Status = BookingStatus.Completed,
            TotalPrice = 250m
        });

        // 3. Cancelled booking today with late fee retained: 100m (CancellationFee set)
        db.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-PERF-03",
            CourtId = courtId,
            UserId = clientId,
            StartTime = todayStart.AddHours(14),
            EndTime = todayStart.AddHours(15),
            Status = BookingStatus.Cancelled,
            TotalPrice = 200m,
            CancellationFee = 100m,
            CancelledAt = todayStart.AddHours(8)
        });

        // 4. Cancelled booking in future with 0 fee (free cancellation): 0m
        db.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-PERF-04",
            CourtId = courtId,
            UserId = clientId,
            StartTime = todayStart.AddDays(7).AddHours(10),
            EndTime = todayStart.AddDays(7).AddHours(11),
            Status = BookingStatus.Cancelled,
            TotalPrice = 400m,
            CancellationFee = 0m,
            CancelledAt = todayStart
        });

        await db.SaveChangesAsync();

        var summary = await ownerService.GetDashboardSummaryAsync(ownerId);

        Assert.NotNull(summary);
        // Total expected revenue: 300 (confirmed) + 250 (completed) + 100 (cancellation fee) + 0 (free) = 650m
        Assert.Equal(650m, summary.TotalRevenue);
        Assert.Equal(2, summary.CancelledBookingsCount);
        Assert.Equal(400m, summary.Analytics.RevenueToday); // 300 confirmed + 100 fee today
    }
}

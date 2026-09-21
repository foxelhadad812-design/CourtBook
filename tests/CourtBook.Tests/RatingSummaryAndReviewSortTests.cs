using CourtBook.Application.Common;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Services;
using Xunit;

namespace CourtBook.Tests;

public class RatingSummaryAndReviewSortTests
{
    [Fact]
    public async Task GetVenueRatingSummary_ReturnsZeroState_WhenVenueHasNoReviews()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var newVenue = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = Guid.NewGuid(),
            Name = "Empty Venue",
            City = "Cairo",
            AverageRating = 0.0,
            TotalReviews = 0
        };
        db.Venues.Add(newVenue);
        await db.SaveChangesAsync();

        var reviewService = new ReviewService(db);
        var summary = await reviewService.GetVenueRatingSummaryAsync(newVenue.Id);

        Assert.Equal(0, summary.TotalReviews);
        Assert.Equal(0.0, summary.AverageRating);
        Assert.Equal(0, summary.FiveStarCount);
        Assert.Equal(0, summary.FourStarCount);
        Assert.Equal(0, summary.ThreeStarCount);
        Assert.Equal(0, summary.TwoStarCount);
        Assert.Equal(0, summary.OneStarCount);
        Assert.Equal(0.0, summary.CourtQualityAverage);
        Assert.Equal(0.0, summary.CleanlinessAverage);
        Assert.Equal(0.0, summary.StaffAverage);
        Assert.Equal(0.0, summary.ValueAverage);
    }

    [Fact]
    public async Task GetVenueRatingSummary_CalculatesDistributionAndCategoryAverages_Accurately()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var reviewService = new ReviewService(db);

        var client2 = new User { Id = Guid.NewGuid(), Name = "Reviewer 2", Email = "r2@test.com", PasswordHash = "h" };
        var client3 = new User { Id = Guid.NewGuid(), Name = "Reviewer 3", Email = "r3@test.com", PasswordHash = "h" };
        var client4 = new User { Id = Guid.NewGuid(), Name = "Reviewer 4", Email = "r4@test.com", PasswordHash = "h" };
        db.Users.AddRange(client2, client3, client4);

        // Create completed bookings for the reviews
        var b1 = new Booking { Id = Guid.NewGuid(), BookingReference = "B1", CourtId = courtId, UserId = clientId, StartTime = DateTime.UtcNow.AddDays(-5), EndTime = DateTime.UtcNow.AddDays(-5).AddHours(1), Status = BookingStatus.Completed, TotalPrice = 100m };
        var b2 = new Booking { Id = Guid.NewGuid(), BookingReference = "B2", CourtId = courtId, UserId = client2.Id, StartTime = DateTime.UtcNow.AddDays(-4), EndTime = DateTime.UtcNow.AddDays(-4).AddHours(1), Status = BookingStatus.Completed, TotalPrice = 100m };
        var b3 = new Booking { Id = Guid.NewGuid(), BookingReference = "B3", CourtId = courtId, UserId = client3.Id, StartTime = DateTime.UtcNow.AddDays(-3), EndTime = DateTime.UtcNow.AddDays(-3).AddHours(1), Status = BookingStatus.Completed, TotalPrice = 100m };
        var b4 = new Booking { Id = Guid.NewGuid(), BookingReference = "B4", CourtId = courtId, UserId = client4.Id, StartTime = DateTime.UtcNow.AddDays(-2), EndTime = DateTime.UtcNow.AddDays(-2).AddHours(1), Status = BookingStatus.Completed, TotalPrice = 100m };
        db.Bookings.AddRange(b1, b2, b3, b4);

        // Seed 4 reviews directly for the venue
        var r1 = new Review
        {
            Id = Guid.NewGuid(),
            VenueId = venueId,
            BookingId = b1.Id,
            UserId = clientId,
            OverallRating = 5,
            CourtQualityRating = 5,
            CleanlinessRating = 5,
            StaffRating = 5,
            ValueRating = 5,
            Comment = "Outstanding facility!",
            CreatedAt = DateTime.UtcNow.AddDays(-4)
        };
        var r2 = new Review
        {
            Id = Guid.NewGuid(),
            VenueId = venueId,
            BookingId = b2.Id,
            UserId = client2.Id,
            OverallRating = 5,
            CourtQualityRating = 4,
            CleanlinessRating = 4,
            StaffRating = 4,
            ValueRating = 4,
            Comment = "Very good overall.",
            CreatedAt = DateTime.UtcNow.AddDays(-3)
        };
        var r3 = new Review
        {
            Id = Guid.NewGuid(),
            VenueId = venueId,
            BookingId = b3.Id,
            UserId = client3.Id,
            OverallRating = 4,
            CourtQualityRating = 4,
            CleanlinessRating = 3,
            StaffRating = 4,
            ValueRating = 4,
            Comment = "Decent court.",
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        };
        var r4 = new Review
        {
            Id = Guid.NewGuid(),
            VenueId = venueId,
            BookingId = b4.Id,
            UserId = client4.Id,
            OverallRating = 2,
            CourtQualityRating = 2,
            CleanlinessRating = 2,
            StaffRating = 3,
            ValueRating = 2,
            Comment = "Needs better maintenance.",
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };

        db.Reviews.AddRange(r1, r2, r3, r4);
        await db.SaveChangesAsync();

        var summary = await reviewService.GetVenueRatingSummaryAsync(venueId);

        Assert.Equal(4, summary.TotalReviews);
        Assert.Equal(4.0, summary.AverageRating); // (5+5+4+2) / 4 = 16 / 4 = 4.0

        // Check distribution counts
        Assert.Equal(2, summary.FiveStarCount);
        Assert.Equal(1, summary.FourStarCount);
        Assert.Equal(0, summary.ThreeStarCount);
        Assert.Equal(1, summary.TwoStarCount);
        Assert.Equal(0, summary.OneStarCount);

        // Check percentages
        Assert.Equal(50.0, summary.FiveStarPercent);
        Assert.Equal(25.0, summary.FourStarPercent);
        Assert.Equal(0.0, summary.ThreeStarPercent);
        Assert.Equal(25.0, summary.TwoStarPercent);
        Assert.Equal(0.0, summary.OneStarPercent);

        // Check category averages
        Assert.Equal(3.8, summary.CourtQualityAverage); // (5+4+4+2)/4 = 3.75 -> rounded to 3.8
        Assert.Equal(3.5, summary.CleanlinessAverage);   // (5+4+3+2)/4 = 3.5
        Assert.Equal(4.0, summary.StaffAverage);         // (5+4+4+3)/4 = 4.0
        Assert.Equal(3.8, summary.ValueAverage);         // (5+4+4+2)/4 = 3.75 -> rounded to 3.8
    }

    [Fact]
    public async Task GetVenueReviews_SortsAndFilters_Correctly()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var reviewService = new ReviewService(db);

        var c2 = new User { Id = Guid.NewGuid(), Name = "User 2", Email = "u2@test.com", PasswordHash = "h" };
        var c3 = new User { Id = Guid.NewGuid(), Name = "User 3", Email = "u3@test.com", PasswordHash = "h" };
        db.Users.AddRange(c2, c3);

        var b1 = new Booking { Id = Guid.NewGuid(), BookingReference = "B11", CourtId = courtId, UserId = clientId, StartTime = DateTime.UtcNow.AddDays(-11), EndTime = DateTime.UtcNow.AddDays(-11).AddHours(1), Status = BookingStatus.Completed, TotalPrice = 100m };
        var b2 = new Booking { Id = Guid.NewGuid(), BookingReference = "B22", CourtId = courtId, UserId = c2.Id, StartTime = DateTime.UtcNow.AddDays(-6), EndTime = DateTime.UtcNow.AddDays(-6).AddHours(1), Status = BookingStatus.Completed, TotalPrice = 100m };
        var b3 = new Booking { Id = Guid.NewGuid(), BookingReference = "B33", CourtId = courtId, UserId = c3.Id, StartTime = DateTime.UtcNow.AddDays(-2), EndTime = DateTime.UtcNow.AddDays(-2).AddHours(1), Status = BookingStatus.Completed, TotalPrice = 100m };
        db.Bookings.AddRange(b1, b2, b3);

        var rev5 = new Review
        {
            Id = Guid.NewGuid(),
            VenueId = venueId,
            BookingId = b1.Id,
            UserId = clientId,
            OverallRating = 5,
            Comment = "5 stars",
            CreatedAt = DateTime.UtcNow.AddDays(-10)
        };
        var rev3 = new Review
        {
            Id = Guid.NewGuid(),
            VenueId = venueId,
            BookingId = b2.Id,
            UserId = c2.Id,
            OverallRating = 3,
            Comment = "3 stars",
            CreatedAt = DateTime.UtcNow.AddDays(-5)
        };
        var rev1 = new Review
        {
            Id = Guid.NewGuid(),
            VenueId = venueId,
            BookingId = b3.Id,
            UserId = c3.Id,
            OverallRating = 1,
            Comment = "1 star",
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        db.Reviews.AddRange(rev5, rev3, rev1);
        await db.SaveChangesAsync();

        var pagedReq = new PagedRequest { Page = 1, PageSize = 10 };

        // 1. Sort by Highest
        var highest = await reviewService.GetVenueReviewsAsync(venueId, pagedReq, sortBy: "highest");
        Assert.Equal(3, highest.TotalCount);
        Assert.Equal(5, highest.Items[0].OverallRating);
        Assert.Equal(3, highest.Items[1].OverallRating);
        Assert.Equal(1, highest.Items[2].OverallRating);

        // 2. Sort by Lowest
        var lowest = await reviewService.GetVenueReviewsAsync(venueId, pagedReq, sortBy: "lowest");
        Assert.Equal(3, lowest.TotalCount);
        Assert.Equal(1, lowest.Items[0].OverallRating);
        Assert.Equal(3, lowest.Items[1].OverallRating);
        Assert.Equal(5, lowest.Items[2].OverallRating);

        // 3. Sort by Recent
        var recent = await reviewService.GetVenueReviewsAsync(venueId, pagedReq, sortBy: "recent");
        Assert.Equal(3, recent.TotalCount);
        Assert.Equal(1, recent.Items[0].OverallRating); // added -1 day
        Assert.Equal(3, recent.Items[1].OverallRating); // added -5 days
        Assert.Equal(5, recent.Items[2].OverallRating); // added -10 days

        // 4. Filter by Rating = 5
        var filtered5 = await reviewService.GetVenueReviewsAsync(venueId, pagedReq, rating: 5);
        Assert.Single(filtered5.Items);
        Assert.Equal(5, filtered5.Items[0].OverallRating);

        // 5. Filter by Rating = 3
        var filtered3 = await reviewService.GetVenueReviewsAsync(venueId, pagedReq, rating: 3);
        Assert.Single(filtered3.Items);
        Assert.Equal(3, filtered3.Items[0].OverallRating);
    }
}

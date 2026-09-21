using CourtBook.Application.Common;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Services;
using Xunit;

namespace CourtBook.Tests;

public class ReviewModerationTests
{
    [Fact]
    public async Task GetVenueReviews_FiltersOut_UnmoderatedReviews()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var reviewService = new ReviewService(db);

        var player2 = new User { Id = Guid.NewGuid(), Name = "Player 2", Email = "p2@test.com", PasswordHash = "h", Role = Role.Client };
        db.Users.Add(player2);

        var b1 = new Booking { Id = Guid.NewGuid(), BookingReference = "B1", CourtId = courtId, UserId = clientId, StartTime = DateTime.UtcNow.AddDays(-2), EndTime = DateTime.UtcNow.AddDays(-2).AddHours(1), Status = BookingStatus.Completed, TotalPrice = 100m };
        var b2 = new Booking { Id = Guid.NewGuid(), BookingReference = "B2", CourtId = courtId, UserId = player2.Id, StartTime = DateTime.UtcNow.AddDays(-3), EndTime = DateTime.UtcNow.AddDays(-3).AddHours(1), Status = BookingStatus.Completed, TotalPrice = 100m };
        db.Bookings.AddRange(b1, b2);

        // Moderated review (5 stars)
        db.Reviews.Add(new Review
        {
            Id = Guid.NewGuid(),
            BookingId = b1.Id,
            UserId = clientId,
            VenueId = venueId,
            OverallRating = 5,
            CourtQualityRating = 5,
            CleanlinessRating = 5,
            StaffRating = 5,
            ValueRating = 5,
            Comment = "Approved review",
            IsModerated = true,
            CreatedAt = DateTime.UtcNow
        });

        // Unmoderated / pending review (1 star)
        db.Reviews.Add(new Review
        {
            Id = Guid.NewGuid(),
            BookingId = b2.Id,
            UserId = player2.Id,
            VenueId = venueId,
            OverallRating = 1,
            CourtQualityRating = 1,
            CleanlinessRating = 1,
            StaffRating = 1,
            ValueRating = 1,
            Comment = "Pending unmoderated review",
            IsModerated = false,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var pagedReviews = await reviewService.GetVenueReviewsAsync(venueId, new PagedRequest { Page = 1, PageSize = 10 });

        // Only the moderated review should appear
        Assert.Single(pagedReviews.Items);
        Assert.Equal("Approved review", pagedReviews.Items[0].Comment);
        Assert.Equal(5, pagedReviews.Items[0].OverallRating);

        // Rating summary should reflect only moderated reviews
        var summary = await reviewService.GetVenueRatingSummaryAsync(venueId);
        Assert.Equal(1, summary.TotalReviews);
        Assert.Equal(5.0, summary.AverageRating);
        Assert.Equal(1, summary.FiveStarCount);
        Assert.Equal(0, summary.OneStarCount);
    }
}

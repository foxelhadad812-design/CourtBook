using CourtBook.Application.DTOs;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Services;
using Xunit;

namespace CourtBook.Tests;

public class ReviewEligibilityTests
{
    [Fact]
    public async Task CreateReview_SucceedsForCompletedBooking_AndUpdatesVenueAverageRating()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var reviewService = new ReviewService(db);

        // Add completed booking
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-REVIEW-001",
            CourtId = courtId,
            UserId = clientId,
            StartTime = DateTime.UtcNow.AddDays(-2),
            EndTime = DateTime.UtcNow.AddDays(-2).AddHours(1),
            Status = BookingStatus.Completed,
            TotalPrice = 200m
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var request = new CreateReviewRequest
        {
            BookingId = booking.Id,
            OverallRating = 5,
            CourtQualityRating = 5,
            CleanlinessRating = 5,
            StaffRating = 4,
            ValueRating = 5,
            Comment = "Excellent court and lighting!"
        };

        var result = await reviewService.CreateAsync(clientId, venueId, request);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value.OverallRating);

        // Verify venue rating updated
        var venue = await db.Venues.FindAsync(venueId);
        Assert.Equal(1, venue!.TotalReviews);
        Assert.Equal(5.0, venue.AverageRating);
    }

    [Fact]
    public async Task CreateReview_RejectsDuplicateReviewForSameBooking()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var reviewService = new ReviewService(db);

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-REVIEW-002",
            CourtId = courtId,
            UserId = clientId,
            StartTime = DateTime.UtcNow.AddDays(-2),
            EndTime = DateTime.UtcNow.AddDays(-2).AddHours(1),
            Status = BookingStatus.Completed,
            TotalPrice = 200m
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var request = new CreateReviewRequest
        {
            BookingId = booking.Id,
            OverallRating = 4,
            CourtQualityRating = 4,
            CleanlinessRating = 4,
            StaffRating = 4,
            ValueRating = 4,
            Comment = "Good experience"
        };

        // First review succeeds
        var firstResult = await reviewService.CreateAsync(clientId, venueId, request);
        Assert.True(firstResult.IsSuccess);

        // Second review for the same booking must return Conflict (409)
        var secondResult = await reviewService.CreateAsync(clientId, venueId, request);
        Assert.True(secondResult.IsFailure);
        Assert.Equal("CONFLICT", secondResult.Error.Code);
    }

    [Fact]
    public async Task CreateReview_RejectsNonCompletedBooking()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var reviewService = new ReviewService(db);

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-REVIEW-003",
            CourtId = courtId,
            UserId = clientId,
            StartTime = DateTime.UtcNow.AddDays(1),
            EndTime = DateTime.UtcNow.AddDays(1).AddHours(1),
            Status = BookingStatus.Confirmed, // Not completed!
            TotalPrice = 200m
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var request = new CreateReviewRequest
        {
            BookingId = booking.Id,
            OverallRating = 5,
            CourtQualityRating = 5,
            CleanlinessRating = 5,
            StaffRating = 5,
            ValueRating = 5
        };

        var result = await reviewService.CreateAsync(clientId, venueId, request);
        Assert.True(result.IsFailure);
        Assert.Equal("BAD_REQUEST", result.Error.Code);
        Assert.Contains("completed", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }
}

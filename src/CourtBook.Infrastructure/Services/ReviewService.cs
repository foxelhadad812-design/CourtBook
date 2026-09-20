using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Infrastructure.Services;

public class ReviewService : IReviewService
{
    private readonly AppDbContext _db;

    public ReviewService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<ReviewResponse>> CreateAsync(Guid userId, Guid venueId, CreateReviewRequest request)
    {
        // 1. Check ratings range
        if (request.OverallRating < 1 || request.OverallRating > 5 ||
            request.CourtQualityRating < 1 || request.CourtQualityRating > 5 ||
            request.CleanlinessRating < 1 || request.CleanlinessRating > 5 ||
            request.StaffRating < 1 || request.StaffRating > 5 ||
            request.ValueRating < 1 || request.ValueRating > 5)
        {
            return Error.Validation("All ratings must be between 1 and 5.");
        }

        // 2. Validate booking existence and relationship
        var booking = await _db.Bookings
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.Id == request.BookingId);

        if (booking is null)
            return Error.NotFound("Booking");

        if (booking.UserId != userId)
            return Error.Forbidden("You can only review your own bookings.");

        if (booking.Court.VenueId != venueId)
            return Error.BadRequest("This booking was not made at the specified venue.");

        if (booking.Status != BookingStatus.Completed)
            return Error.BadRequest("Only completed bookings can be reviewed.");

        // 3. Prevent duplicate reviews for the same booking
        var alreadyReviewed = await _db.Reviews.AnyAsync(r => r.BookingId == request.BookingId);
        if (alreadyReviewed)
            return Error.Conflict("A review has already been submitted for this booking.");

        var venue = await _db.Venues.FindAsync(venueId);
        if (venue is null)
            return Error.NotFound("Venue");

        var review = new Review
        {
            Id = Guid.NewGuid(),
            BookingId = request.BookingId,
            UserId = userId,
            VenueId = venueId,
            OverallRating = request.OverallRating,
            CourtQualityRating = request.CourtQualityRating,
            CleanlinessRating = request.CleanlinessRating,
            StaffRating = request.StaffRating,
            ValueRating = request.ValueRating,
            Comment = request.Comment,
            CreatedAt = DateTime.UtcNow
        };

        _db.Reviews.Add(review);
        await _db.SaveChangesAsync();

        // 4. Update Venue AverageRating and TotalReviews
        var ratings = await _db.Reviews
            .Where(r => r.VenueId == venueId)
            .Select(r => r.OverallRating)
            .ToListAsync();

        venue.TotalReviews = ratings.Count;
        venue.AverageRating = Math.Round(ratings.Average(), 1);

        await _db.SaveChangesAsync();

        var user = await _db.Users.FindAsync(userId);
        return Result<ReviewResponse>.Ok(new ReviewResponse
        {
            Id = review.Id,
            BookingId = review.BookingId,
            UserId = review.UserId,
            UserName = user?.Name ?? "Verified Player",
            VenueId = review.VenueId,
            OverallRating = review.OverallRating,
            CourtQualityRating = review.CourtQualityRating,
            CleanlinessRating = review.CleanlinessRating,
            StaffRating = review.StaffRating,
            ValueRating = review.ValueRating,
            Comment = review.Comment,
            CreatedAt = review.CreatedAt
        });
    }

    public async Task<PagedResult<ReviewResponse>> GetVenueReviewsAsync(Guid venueId, PagedRequest request)
    {
        var query = _db.Reviews
            .AsNoTracking()
            .Include(r => r.User)
            .Where(r => r.VenueId == venueId)
            .OrderByDescending(r => r.CreatedAt);

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(r => new ReviewResponse
            {
                Id = r.Id,
                BookingId = r.BookingId,
                UserId = r.UserId,
                UserName = r.User.Name,
                VenueId = r.VenueId,
                OverallRating = r.OverallRating,
                CourtQualityRating = r.CourtQualityRating,
                CleanlinessRating = r.CleanlinessRating,
                StaffRating = r.StaffRating,
                ValueRating = r.ValueRating,
                Comment = r.Comment,
                OwnerResponse = r.OwnerResponse,
                OwnerRespondedAt = r.OwnerRespondedAt,
                CreatedAt = r.CreatedAt
            })
            .ToListAsync();

        return PagedResult<ReviewResponse>.From(items, totalCount, request.Page, request.PageSize);
    }

    public async Task<Result> AddOwnerResponseAsync(Guid ownerId, Guid venueId, Guid reviewId, OwnerResponseRequest request)
    {
        var venue = await _db.Venues.FindAsync(venueId);
        if (venue is null)
            return Error.NotFound("Venue");

        if (venue.OwnerId != ownerId)
            return Error.Forbidden("Only the facility owner can respond to reviews.");

        var review = await _db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId && r.VenueId == venueId);
        if (review is null)
            return Error.NotFound("Review");

        review.OwnerResponse = request.Response;
        review.OwnerRespondedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Result.Ok();
    }
}

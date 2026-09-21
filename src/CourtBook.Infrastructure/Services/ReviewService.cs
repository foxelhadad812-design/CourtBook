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
            IsModerated = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Reviews.Add(review);
        await _db.SaveChangesAsync();

        // 4. Update Venue AverageRating and TotalReviews from moderated reviews only
        var ratings = await _db.Reviews
            .Where(r => r.VenueId == venueId && r.IsModerated)
            .Select(r => r.OverallRating)
            .ToListAsync();

        venue.TotalReviews = ratings.Count;
        venue.AverageRating = ratings.Count > 0 ? Math.Round(ratings.Average(), 1) : 0.0;

        await _db.SaveChangesAsync();

        var user = await _db.Users.FindAsync(userId);
        return Result<ReviewResponse>.Ok(new ReviewResponse
        {
            Id = review.Id,
            BookingId = review.BookingId,
            UserId = review.UserId,
            UserName = user?.Name ?? "Verified Player",
            VenueId = review.VenueId,
            CourtName = booking.Court?.Name,
            IsVerifiedBooking = true,
            OverallRating = review.OverallRating,
            CourtQualityRating = review.CourtQualityRating,
            CleanlinessRating = review.CleanlinessRating,
            StaffRating = review.StaffRating,
            ValueRating = review.ValueRating,
            Comment = review.Comment,
            CreatedAt = review.CreatedAt
        });
    }

    public async Task<PagedResult<ReviewResponse>> GetVenueReviewsAsync(Guid venueId, PagedRequest request, string? sortBy = null, int? rating = null)
    {
        var query = _db.Reviews
            .AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.Booking)
                .ThenInclude(b => b.Court)
            .Where(r => r.VenueId == venueId && r.IsModerated);

        if (rating.HasValue && rating.Value >= 1 && rating.Value <= 5)
        {
            query = query.Where(r => r.OverallRating == rating.Value);
        }

        query = (sortBy?.ToLowerInvariant()) switch
        {
            "highest" => query.OrderByDescending(r => r.OverallRating).ThenByDescending(r => r.CreatedAt),
            "lowest" => query.OrderBy(r => r.OverallRating).ThenByDescending(r => r.CreatedAt),
            "recent" or _ => query.OrderByDescending(r => r.CreatedAt)
        };

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
                CourtName = r.Booking.Court != null ? r.Booking.Court.Name : null,
                IsVerifiedBooking = true,
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

    public async Task<VenueRatingSummaryDto> GetVenueRatingSummaryAsync(Guid venueId)
    {
        var reviews = await _db.Reviews
            .AsNoTracking()
            .Where(r => r.VenueId == venueId && r.IsModerated)
            .ToListAsync();

        var total = reviews.Count;
        if (total == 0)
        {
            var venue = await _db.Venues.FindAsync(venueId);
            return new VenueRatingSummaryDto
            {
                VenueId = venueId,
                AverageRating = venue?.AverageRating ?? 0.0,
                TotalReviews = 0,
                FiveStarCount = 0,
                FourStarCount = 0,
                ThreeStarCount = 0,
                TwoStarCount = 0,
                OneStarCount = 0,
                FiveStarPercent = 0,
                FourStarPercent = 0,
                ThreeStarPercent = 0,
                TwoStarPercent = 0,
                OneStarPercent = 0,
                CourtQualityAverage = 0,
                CleanlinessAverage = 0,
                StaffAverage = 0,
                ValueAverage = 0
            };
        }

        var c5 = reviews.Count(r => r.OverallRating == 5);
        var c4 = reviews.Count(r => r.OverallRating == 4);
        var c3 = reviews.Count(r => r.OverallRating == 3);
        var c2 = reviews.Count(r => r.OverallRating == 2);
        var c1 = reviews.Count(r => r.OverallRating == 1);

        return new VenueRatingSummaryDto
        {
            VenueId = venueId,
            AverageRating = Math.Round(reviews.Average(r => r.OverallRating), 1),
            TotalReviews = total,
            FiveStarCount = c5,
            FourStarCount = c4,
            ThreeStarCount = c3,
            TwoStarCount = c2,
            OneStarCount = c1,
            FiveStarPercent = Math.Round((double)c5 / total * 100, 1),
            FourStarPercent = Math.Round((double)c4 / total * 100, 1),
            ThreeStarPercent = Math.Round((double)c3 / total * 100, 1),
            TwoStarPercent = Math.Round((double)c2 / total * 100, 1),
            OneStarPercent = Math.Round((double)c1 / total * 100, 1),
            CourtQualityAverage = Math.Round(reviews.Average(r => r.CourtQualityRating), 1),
            CleanlinessAverage = Math.Round(reviews.Average(r => r.CleanlinessRating), 1),
            StaffAverage = Math.Round(reviews.Average(r => r.StaffRating), 1),
            ValueAverage = Math.Round(reviews.Average(r => r.ValueRating), 1)
        };
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

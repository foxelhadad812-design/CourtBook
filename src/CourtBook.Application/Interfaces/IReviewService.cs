using CourtBook.Application.Common;
using CourtBook.Application.DTOs;

namespace CourtBook.Application.Interfaces;

public interface IReviewService
{
    Task<Result<ReviewResponse>> CreateAsync(Guid userId, Guid venueId, CreateReviewRequest request);
    Task<PagedResult<ReviewResponse>> GetVenueReviewsAsync(Guid venueId, PagedRequest request, string? sortBy = null, int? rating = null);
    Task<VenueRatingSummaryDto> GetVenueRatingSummaryAsync(Guid venueId);
    Task<Result> AddOwnerResponseAsync(Guid ownerId, Guid venueId, Guid reviewId, OwnerResponseRequest request);
}

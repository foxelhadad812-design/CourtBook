using CourtBook.Application.Common;
using CourtBook.Application.DTOs;

namespace CourtBook.Application.Interfaces;

public interface IOwnerService
{
    Task<OwnerDashboardSummaryDto> GetDashboardSummaryAsync(Guid ownerId);
    Task<List<OwnerVenueDto>> GetOwnerVenuesAsync(Guid ownerId);
    Task<OwnerVenueDto?> GetOwnerVenueByIdAsync(Guid ownerId, Guid venueId);
    Task<PagedResult<OwnerBookingDto>> GetOwnerBookingsAsync(Guid ownerId, OwnerBookingQueryRequest request);
    Task<OwnerBookingDto?> GetOwnerBookingByIdAsync(Guid ownerId, Guid bookingId);
    Task<OwnerAnalyticsDto> GetOwnerAnalyticsAsync(Guid ownerId);
}

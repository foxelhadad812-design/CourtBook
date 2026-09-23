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

    // Phase 4B: Venue Management
    Task<OwnerVenueDetailsDto?> GetOwnerVenueDetailsAsync(Guid ownerId, Guid venueId);
    Task<OwnerVenueDto> CreateVenueAsync(Guid ownerId, CreateVenueRequest request);
    Task<OwnerVenueDto> UpdateVenueAsync(Guid ownerId, Guid venueId, UpdateVenueRequest request);
    Task<DeactivateResultDto> DeactivateVenueAsync(Guid ownerId, Guid venueId);

    // Phase 4B: Court Management
    Task<List<CourtResponse>> GetOwnerVenueCourtsAsync(Guid ownerId, Guid venueId);
    Task<CourtResponse?> GetOwnerCourtByIdAsync(Guid ownerId, Guid courtId);
    Task<CourtResponse> CreateCourtAsync(Guid ownerId, Guid venueId, CreateCourtRequest request);
    Task<CourtResponse> UpdateCourtAsync(Guid ownerId, Guid courtId, UpdateCourtRequest request);
    Task<DeactivateResultDto> DeactivateCourtAsync(Guid ownerId, Guid courtId);

    // Phase 4B: Amenities Management
    Task<List<AmenityDto>> GetAmenitiesCatalogAsync();
    Task<List<VenueAmenityDto>> UpdateVenueAmenitiesAsync(Guid ownerId, Guid venueId, List<Guid> amenityIds);

    // Phase 4B: Image Management
    Task<VenueImageDto> AddVenueImageAsync(Guid ownerId, Guid venueId, AddVenueImageRequest request);
    Task<bool> DeleteVenueImageAsync(Guid ownerId, Guid venueId, Guid imageId);
    Task<bool> SetPrimaryVenueImageAsync(Guid ownerId, Guid venueId, Guid imageId);

    // Phase 4B: Operating Hours Management
    Task<List<OperatingHourDto>> GetVenueOperatingHoursAsync(Guid ownerId, Guid venueId);
    Task<List<OperatingHourDto>> UpdateVenueOperatingHoursAsync(Guid ownerId, Guid venueId, List<UpdateOperatingHourRequest> hours);

    // Phase 12: Manual / Phone Booking & Receptionist Quick Check-in
    Task<BookingResponse> CreateManualBookingAsync(Guid ownerId, CreateManualBookingRequest request);
    Task<QuickCheckInResult> QuickCheckInAsync(Guid ownerId, QuickCheckInRequest request);
}

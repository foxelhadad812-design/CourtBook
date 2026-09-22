using CourtBook.Application.DTOs;
using CourtBook.Domain.Enums;

namespace CourtBook.Application.Interfaces;

public interface IAdminVenueService
{
    Task<AdminDashboardDto> GetDashboardAsync();
    Task<List<AdminVenueDto>> GetAllVenuesAsync(VenueApprovalStatus? status = null, string? search = null);
    Task<AdminVenueDetailsDto?> GetVenueDetailsAsync(Guid venueId);
    Task<AdminVenueDto?> ApproveVenueAsync(Guid adminId, Guid venueId);
    Task<AdminVenueDto?> RejectVenueAsync(Guid adminId, Guid venueId, RejectVenueRequest request);
}

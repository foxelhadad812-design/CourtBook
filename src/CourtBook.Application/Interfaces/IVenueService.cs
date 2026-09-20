using CourtBook.Application.DTOs;

namespace CourtBook.Application.Interfaces;

public interface IVenueService
{
    Task<List<VenueResponse>> GetAllAsync();
    Task<VenueResponse?> GetByIdAsync(Guid id);
    Task<VenueResponse> CreateAsync(Guid ownerId, CreateVenueRequest request);
    Task<VenueResponse?> UpdateAsync(Guid ownerId, string ownerRole, Guid id, UpdateVenueRequest request);
    Task<bool> DeleteAsync(Guid userId, string userRole, Guid id);
}

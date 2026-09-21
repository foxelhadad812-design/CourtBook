using CourtBook.Application.DTOs;

namespace CourtBook.Application.Interfaces;

public interface ICourtService
{
    Task<List<CourtResponse>> GetAllByVenueAsync(Guid venueId);
    Task<CourtResponse?> GetByIdAsync(Guid venueId, Guid id);
    Task<CourtResponse?> GetByIdAsync(Guid id);
    Task<CourtResponse?> CreateAsync(Guid ownerId, Guid venueId, CreateCourtRequest request);
    Task<CourtResponse?> UpdateAsync(Guid ownerId, Guid venueId, Guid id, UpdateCourtRequest request);
    Task<bool> DeleteAsync(Guid userId, string userRole, Guid venueId, Guid id);

    // Schedules
    Task<ScheduleResponse?> AddScheduleAsync(Guid ownerId, Guid venueId, Guid courtId, CreateScheduleRequest request);
    Task<bool> DeleteScheduleAsync(Guid ownerId, Guid venueId, Guid courtId, Guid scheduleId);
}

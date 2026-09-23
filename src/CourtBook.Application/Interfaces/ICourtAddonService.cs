using CourtBook.Application.DTOs;

namespace CourtBook.Application.Interfaces;

public interface ICourtAddonService
{
    Task<List<CourtAddonDto>> GetAddonsByCourtIdAsync(Guid courtId, bool onlyAvailable = true);
    Task<CourtAddonDto> CreateCourtAddonAsync(Guid ownerId, CreateCourtAddonDto dto);
    Task<CourtAddonDto> UpdateCourtAddonAsync(Guid ownerId, Guid addonId, UpdateCourtAddonDto dto);
    Task<bool> DeleteCourtAddonAsync(Guid ownerId, Guid addonId);
}

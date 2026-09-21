using CourtBook.Application.DTOs;

namespace CourtBook.Application.Interfaces;

public interface IProfileService
{
    Task<UserProfileResponse?> GetProfileAsync(Guid userId);
    Task<UserProfileResponse?> UpdateProfileAsync(Guid userId, UpdateProfileRequest request);
    Task<PlayerPreferenceDto?> GetPreferencesAsync(Guid userId);
    Task<PlayerPreferenceDto?> UpdatePreferencesAsync(Guid userId, UpdatePreferencesRequest request);
}

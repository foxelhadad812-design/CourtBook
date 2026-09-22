using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Infrastructure.Services;

public class ProfileService : IProfileService
{
    private readonly AppDbContext _db;

    public ProfileService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<UserProfileResponse?> GetProfileAsync(Guid userId)
    {
        var user = await _db.Users
            .Include(u => u.Profile)
            .Include(u => u.Preference)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user is null) return null;

        // Ensure 1:1 Profile and Preference exist
        var profile = user.Profile;
        if (profile is null)
        {
            profile = new UserProfile
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                SkillLevel = SkillLevel.Beginner,
                CreatedAt = DateTime.UtcNow
            };
            _db.UserProfiles.Add(profile);
            user.Profile = profile;
        }

        var preference = user.Preference;
        if (preference is null)
        {
            preference = new PlayerPreference
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                PreferredCities = "Cairo",
                PreferredDays = "Friday,Saturday",
                PreferredTimeOfDay = "Evening",
                UpdatedAt = DateTime.UtcNow
            };
            _db.PlayerPreferences.Add(preference);
            user.Preference = preference;
        }

        // Count completed bookings as matches played if not manually set
        var completedBookings = await _db.Bookings
            .CountAsync(b => b.UserId == userId && (b.Status == BookingStatus.Completed || (b.Status == BookingStatus.Confirmed && b.EndTime <= DateTime.UtcNow)));

        profile.MatchesPlayedCount = Math.Max(profile.MatchesPlayedCount, completedBookings);

        await _db.SaveChangesAsync();

        return MapToResponse(user, profile, preference);
    }

    public async Task<UserProfileResponse?> UpdateProfileAsync(Guid userId, UpdateProfileRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Name cannot be empty.");

        var user = await _db.Users
            .Include(u => u.Profile)
            .Include(u => u.Preference)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user is null) return null;

        user.Name = request.Name.Trim();
        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            user.Phone = request.Phone.Trim();
        }

        // Update Profile
        var profile = user.Profile ?? new UserProfile { Id = Guid.NewGuid(), UserId = user.Id };
        profile.Bio = request.Bio?.Trim();

        if (!string.IsNullOrWhiteSpace(request.SkillLevel) && Enum.TryParse<SkillLevel>(request.SkillLevel, true, out var skill))
        {
            profile.SkillLevel = skill;
        }

        if (!string.IsNullOrWhiteSpace(request.PreferredSport) && Enum.TryParse<SportType>(request.PreferredSport, true, out var sport))
        {
            profile.PreferredSport = sport;
        }

        profile.UpdatedAt = DateTime.UtcNow;

        if (user.Profile is null)
        {
            _db.UserProfiles.Add(profile);
            user.Profile = profile;
        }

        // Update Preferences
        var preference = user.Preference ?? new PlayerPreference { Id = Guid.NewGuid(), UserId = user.Id };
        if (request.PreferredCities is not null)
        {
            preference.PreferredCities = string.Join(",", request.PreferredCities.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()));
        }
        if (request.PreferredDays is not null)
        {
            preference.PreferredDays = string.Join(",", request.PreferredDays.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()));
        }
        if (request.PreferredTimeOfDay is not null)
        {
            preference.PreferredTimeOfDay = string.Join(",", request.PreferredTimeOfDay.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()));
        }

        preference.UpdatedAt = DateTime.UtcNow;

        if (user.Preference is null)
        {
            _db.PlayerPreferences.Add(preference);
            user.Preference = preference;
        }

        await _db.SaveChangesAsync();

        return MapToResponse(user, profile, preference);
    }

    public async Task<PlayerPreferenceDto?> GetPreferencesAsync(Guid userId)
    {
        var preference = await _db.PlayerPreferences.FirstOrDefaultAsync(p => p.UserId == userId);
        if (preference is null)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user is null) return null;

            preference = new PlayerPreference
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PreferredCities = "Cairo",
                PreferredDays = "Friday,Saturday",
                PreferredTimeOfDay = "Evening",
                UpdatedAt = DateTime.UtcNow
            };
            _db.PlayerPreferences.Add(preference);
            await _db.SaveChangesAsync();
        }

        return MapToPreferenceDto(preference);
    }

    public async Task<PlayerPreferenceDto?> UpdatePreferencesAsync(Guid userId, UpdatePreferencesRequest request)
    {
        var preference = await _db.PlayerPreferences.FirstOrDefaultAsync(p => p.UserId == userId);
        if (preference is null)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user is null) return null;

            preference = new PlayerPreference
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                UpdatedAt = DateTime.UtcNow
            };
            _db.PlayerPreferences.Add(preference);
        }

        preference.PreferredCities = string.Join(",", request.PreferredCities.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()));
        preference.PreferredDays = string.Join(",", request.PreferredDays.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()));
        preference.PreferredTimeOfDay = string.Join(",", request.PreferredTimeOfDay.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()));
        preference.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return MapToPreferenceDto(preference);
    }

    private static UserProfileResponse MapToResponse(User user, UserProfile profile, PlayerPreference preference)
    {
        return new UserProfileResponse
        {
            UserId = user.Id,
            Name = user.Name,
            Email = user.Email,
            Phone = user.Phone,
            Role = user.Role.ToString(),
            MemberSince = user.CreatedAt,
            AvatarUrl = profile.AvatarUrl,
            Bio = profile.Bio,
            SkillLevel = profile.SkillLevel.ToString(),
            PreferredSport = profile.PreferredSport?.ToString(),
            MatchesPlayedCount = profile.MatchesPlayedCount,
            Preferences = MapToPreferenceDto(preference)
        };
    }

    private static PlayerPreferenceDto MapToPreferenceDto(PlayerPreference preference)
    {
        return new PlayerPreferenceDto
        {
            UserId = preference.UserId,
            PreferredSports = SplitString(preference.PreferredSports),
            PreferredCities = SplitString(preference.PreferredCities),
            PreferredDays = SplitString(preference.PreferredDays),
            PreferredTimeOfDay = SplitString(preference.PreferredTimeOfDay),
            PreferredGameType = preference.PreferredGameType,
            MaxDistanceKm = preference.MaxDistanceKm,
            PreferredSkillLevel = preference.PreferredSkillLevel?.ToString(),
            UpdatedAt = preference.UpdatedAt
        };
    }

    private static List<string> SplitString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}

namespace CourtBook.Application.DTOs;

public class UserProfileResponse
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime MemberSince { get; set; }

    // Profile details
    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }
    public string SkillLevel { get; set; } = "Beginner";
    public string? PreferredSport { get; set; }
    public int MatchesPlayedCount { get; set; }

    // Player Preferences
    public PlayerPreferenceDto Preferences { get; set; } = new();
}

public class PlayerPreferenceDto
{
    public Guid UserId { get; set; }
    public List<string> PreferredSports { get; set; } = [];
    public List<string> PreferredCities { get; set; } = [];
    public List<string> PreferredDays { get; set; } = [];
    public List<string> PreferredTimeOfDay { get; set; } = [];
    public string PreferredGameType { get; set; } = string.Empty;
    public int? MaxDistanceKm { get; set; }
    public string? PreferredSkillLevel { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpdateProfileRequest
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Bio { get; set; }
    public string? SkillLevel { get; set; }
    public string? PreferredSport { get; set; }
    public List<string>? PreferredCities { get; set; }
    public List<string>? PreferredDays { get; set; }
    public List<string>? PreferredTimeOfDay { get; set; }
}

public class UpdatePreferencesRequest
{
    public List<string> PreferredCities { get; set; } = [];
    public List<string> PreferredDays { get; set; } = [];
    public List<string> PreferredTimeOfDay { get; set; } = [];
}

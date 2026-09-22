using CourtBook.Domain.Enums;

namespace CourtBook.Domain.Entities;

public class PlayerPreference
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string PreferredSports { get; set; } = string.Empty;    // Comma-separated e.g. "Football,Padel"
    public string PreferredCities { get; set; } = string.Empty;    // Comma-separated or JSON
    public string PreferredDays { get; set; } = string.Empty;      // e.g. "Friday,Saturday"
    public string PreferredTimeOfDay { get; set; } = string.Empty; // e.g. "Evening,Night"
    public string PreferredGameType { get; set; } = string.Empty;  // e.g. "Casual,Competitive"
    public int? MaxDistanceKm { get; set; }
    public SkillLevel? PreferredSkillLevel { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public User User { get; set; } = null!;
}

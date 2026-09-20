namespace CourtBook.Domain.Entities;

public class PlayerPreference
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string PreferredCities { get; set; } = string.Empty; // Comma-separated or JSON
    public string PreferredDays { get; set; } = string.Empty;   // e.g. "Friday,Saturday"
    public string PreferredTimeOfDay { get; set; } = string.Empty; // e.g. "Evening,Night"
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public User User { get; set; } = null!;
}

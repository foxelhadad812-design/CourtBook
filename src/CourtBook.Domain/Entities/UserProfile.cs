using CourtBook.Domain.Enums;

namespace CourtBook.Domain.Entities;

public class UserProfile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }
    public SkillLevel SkillLevel { get; set; } = SkillLevel.Beginner;
    public SportType? PreferredSport { get; set; }
    public int MatchesPlayedCount { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation property
    public User User { get; set; } = null!;
}

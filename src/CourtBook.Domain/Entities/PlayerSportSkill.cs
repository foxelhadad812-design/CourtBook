using CourtBook.Domain.Enums;

namespace CourtBook.Domain.Entities;

/// <summary>
/// Represents a player's sport-specific skill level and rating.
/// Supports multi-sport proficiency tracking and deterministic team balancing.
/// </summary>
public class PlayerSportSkill
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public SportType SportType { get; set; }
    public SkillLevel SkillLevel { get; set; } = SkillLevel.Beginner;

    /// <summary>
    /// Numerical skill score used for deterministic team balancing (e.g. 1000 = Beginner, 1500 = Intermediate, 2000 = Advanced).
    /// </summary>
    public int SkillScore { get; set; } = 1000;

    public int MatchesPlayed { get; set; } = 0;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public User User { get; set; } = null!;
}

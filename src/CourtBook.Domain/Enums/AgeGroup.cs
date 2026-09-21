namespace CourtBook.Domain.Enums;

/// <summary>
/// Age classification for community matches and events.
/// </summary>
public enum AgeGroup
{
    AllAges = 0,
    Kids = 1,      // 6 - 12 years
    Juniors = 2,   // 13 - 15 years
    Teens = 3,     // 16 - 17 years
    Adults = 4,    // 18+ years
    Custom = 5     // Explicit MinAge and/or MaxAge
}

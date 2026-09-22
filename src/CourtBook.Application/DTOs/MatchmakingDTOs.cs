using CourtBook.Application.Common;

namespace CourtBook.Application.DTOs;

public class PlayerSportSkillDto
{
    public Guid Id { get; set; }
    public string SportType { get; set; } = string.Empty;
    public string SkillLevel { get; set; } = "Beginner";
    public int SkillScore { get; set; } = 1000;
    public int MatchesPlayed { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpsertPlayerSportSkillRequest
{
    public string SportType { get; set; } = string.Empty;
    public string SkillLevel { get; set; } = "Beginner";
    public int? SkillScore { get; set; }
}

public class UpdatePlayerPreferenceRequest
{
    public List<string>? PreferredSports { get; set; }
    public List<string>? PreferredCities { get; set; }
    public List<string>? PreferredDays { get; set; }
    public List<string>? PreferredTimeOfDay { get; set; }
    public string? PreferredGameType { get; set; }
    public int? MaxDistanceKm { get; set; }
    public string? PreferredSkillLevel { get; set; }
}

public class MatchScoreBreakdownDto
{
    public double SportScore { get; set; }       // max 40
    public double SkillScore { get; set; }       // max 25
    public double TimeScore { get; set; }        // max 20
    public double LocationScore { get; set; }    // max 15
    public double TotalScore => SportScore + SkillScore + TimeScore + LocationScore; // max 100
}

public class MatchmakingRecommendationDto
{
    public GameResponse Game { get; set; } = null!;
    public double MatchScore { get; set; }
    public MatchScoreBreakdownDto Breakdown { get; set; } = new();
    public string Explanation { get; set; } = string.Empty;
}

public class MatchmakingQueryRequest
{
    public string? Sport { get; set; }
    public string? City { get; set; }
    public DateOnly? Date { get; set; }
    public string? SkillLevel { get; set; }
}

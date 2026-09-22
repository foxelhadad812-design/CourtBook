using CourtBook.Application.Common;

namespace CourtBook.Application.DTOs;

public class CreateGameRequest
{
    public string Title { get; set; } = string.Empty;
    public string SportType { get; set; } = string.Empty;
    public Guid VenueId { get; set; }
    public Guid CourtId { get; set; }
    public DateOnly Date { get; set; }
    public string StartTime { get; set; } = "18:00"; // HH:mm
    public string EndTime { get; set; } = "19:30";   // HH:mm
    public string SkillLevel { get; set; } = "AllLevels";
    public string AgeGroup { get; set; } = "AllAges";
    public int? MinAge { get; set; }
    public int? MaxAge { get; set; }
    public int MaxPlayers { get; set; } = 10;
    public int MinPlayers { get; set; } = 2;
    public decimal PricePerPlayer { get; set; } = 0;
    public string? Description { get; set; }
    public bool IsPrivate { get; set; } = false;
    public string? AccessCode { get; set; }
    public bool HasTeams { get; set; } = true;
}

public class GameSearchRequest : PagedRequest
{
    public string? Sport { get; set; }
    public string? City { get; set; }
    public string? SkillLevel { get; set; }
    public string? AgeGroup { get; set; }
    public DateOnly? Date { get; set; }
    public string? Status { get; set; }
    public bool? IncludePrivate { get; set; }
}

public class JoinGameRequest
{
    public string? AccessCode { get; set; }
}

public class GameParticipantDto
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime JoinedAt { get; set; }
    public string? Team { get; set; }
    public bool IsReady { get; set; }
    public int SkillScore { get; set; } = 1000;
    public string SkillLevel { get; set; } = "Beginner";
}

public class GameResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string SportType { get; set; } = string.Empty;
    public Guid VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string VenueCity { get; set; } = string.Empty;
    public Guid CourtId { get; set; }
    public string CourtName { get; set; } = string.Empty;
    public Guid CreatorId { get; set; }
    public string CreatorName { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public string SkillLevel { get; set; } = string.Empty;
    public string AgeGroup { get; set; } = "AllAges";
    public int? MinAge { get; set; }
    public int? MaxAge { get; set; }
    public string AgeDisplay { get; set; } = "All Ages";
    public int MaxPlayers { get; set; }
    public int MinPlayers { get; set; }
    public int CurrentPlayersCount { get; set; }
    public decimal PricePerPlayer { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsPrivate { get; set; }
    public string? AccessCode { get; set; }
    public bool HasTeams { get; set; }
    public bool AllPlayersReady { get; set; }
    public List<GameParticipantDto> Participants { get; set; } = [];
    public List<GameParticipantDto> TeamAPlayers => Participants.Where(p => p.Team == "TeamA").ToList();
    public List<GameParticipantDto> TeamBPlayers => Participants.Where(p => p.Team == "TeamB").ToList();
    public DateTime CreatedAt { get; set; }
}

public class SetPlayerReadyRequest
{
    public bool IsReady { get; set; } = true;
}

public class AssignTeamRequest
{
    public Guid ParticipantUserId { get; set; }
    public string? Team { get; set; } // "TeamA", "TeamB", or null
}

public class BalanceTeamsResponse
{
    public Guid GameId { get; set; }
    public string TeamAName { get; set; } = "Team A";
    public string TeamBName { get; set; } = "Team B";
    public int TeamATotalScore { get; set; }
    public int TeamBTotalScore { get; set; }
    public int ScoreDifference => Math.Abs(TeamATotalScore - TeamBTotalScore);
    public List<GameParticipantDto> TeamAPlayers { get; set; } = [];
    public List<GameParticipantDto> TeamBPlayers { get; set; } = [];
}

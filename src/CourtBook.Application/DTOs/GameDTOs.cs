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
}

public class GameSearchRequest : PagedRequest
{
    public string? Sport { get; set; }
    public string? City { get; set; }
    public string? SkillLevel { get; set; }
    public string? AgeGroup { get; set; }
    public DateOnly? Date { get; set; }
    public string? Status { get; set; }
}

public class GameParticipantDto
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime JoinedAt { get; set; }
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
    public List<GameParticipantDto> Participants { get; set; } = [];
    public DateTime CreatedAt { get; set; }
}

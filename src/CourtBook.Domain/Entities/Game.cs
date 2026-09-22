using CourtBook.Domain.Enums;

namespace CourtBook.Domain.Entities;

public class Game
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public SportType SportType { get; set; }
    public Guid VenueId { get; set; }
    public Guid CourtId { get; set; }
    public Guid CreatorId { get; set; }
    
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    
    public SkillLevel SkillLevel { get; set; } = SkillLevel.AllLevels;
    public AgeGroup AgeGroup { get; set; } = AgeGroup.AllAges;
    public int? MinAge { get; set; }
    public int? MaxAge { get; set; }
    public int MaxPlayers { get; set; }
    public int MinPlayers { get; set; } = 2;
    public decimal PricePerPlayer { get; set; } = 0;
    public GameStatus Status { get; set; } = GameStatus.Open;
    public string? Description { get; set; }
    public bool IsPrivate { get; set; } = false;
    public string? AccessCode { get; set; }
    public bool HasTeams { get; set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Venue Venue { get; set; } = null!;
    public Court Court { get; set; } = null!;
    public User Creator { get; set; } = null!;
    public ICollection<GameParticipant> Participants { get; set; } = [];
    public ICollection<GameInvitation> Invitations { get; set; } = [];
}

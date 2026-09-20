namespace CourtBook.Domain.Entities;

public class GameParticipant
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public Guid UserId { get; set; }
    public bool IsConfirmed { get; set; } = true;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Game Game { get; set; } = null!;
    public User User { get; set; } = null!;
}

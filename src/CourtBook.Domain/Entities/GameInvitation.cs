using CourtBook.Domain.Enums;

namespace CourtBook.Domain.Entities;

public class GameInvitation
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public Guid InviterId { get; set; }
    public Guid InviteeId { get; set; }
    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;
    public string? Message { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    // Navigation properties
    public Game Game { get; set; } = null!;
    public User Inviter { get; set; } = null!;
    public User Invitee { get; set; } = null!;
}

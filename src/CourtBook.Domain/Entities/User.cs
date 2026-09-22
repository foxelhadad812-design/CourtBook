using CourtBook.Domain.Enums;

namespace CourtBook.Domain.Entities;

/// <summary>
/// Represents a registered user (Admin, Owner, or Client).
/// </summary>
public class User
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public Role Role { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public UserProfile? Profile { get; set; }
    public PlayerPreference? Preference { get; set; }
    public ICollection<Venue> OwnedVenues { get; set; } = [];
    public ICollection<Booking> Bookings { get; set; } = [];
    public ICollection<Review> Reviews { get; set; } = [];
    public ICollection<Favorite> Favorites { get; set; } = [];
    public ICollection<Game> CreatedGames { get; set; } = [];
    public ICollection<GameParticipant> GameParticipations { get; set; } = [];
    public ICollection<Notification> Notifications { get; set; } = [];
    public ICollection<AuditLog> AuditLogs { get; set; } = [];
    public ICollection<TermsAcceptance> TermsAcceptances { get; set; } = [];
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
    public ICollection<PlayerSportSkill> SportSkills { get; set; } = [];
    public ICollection<GameInvitation> SentInvitations { get; set; } = [];
    public ICollection<GameInvitation> ReceivedInvitations { get; set; } = [];
    public ICollection<PlayerConnection> SentConnections { get; set; } = [];
    public ICollection<PlayerConnection> ReceivedConnections { get; set; } = [];
    public OwnerBalance? OwnerBalance { get; set; }
    public ICollection<OwnerPayoutMethod> PayoutMethods { get; set; } = [];
    public ICollection<PayoutRequest> PayoutRequests { get; set; } = [];
    public ICollection<RecoveryObligation> RecoveryObligations { get; set; } = [];
}

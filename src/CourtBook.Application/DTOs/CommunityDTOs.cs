using CourtBook.Application.Common;
using CourtBook.Domain.Enums;

namespace CourtBook.Application.DTOs;

public class GameInvitationDto
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public string GameTitle { get; set; } = string.Empty;
    public string SportType { get; set; } = string.Empty;
    public DateOnly GameDate { get; set; }
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public string VenueName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public decimal PricePerPlayer { get; set; }
    public bool IsPrivate { get; set; }
    public Guid InviterId { get; set; }
    public string InviterName { get; set; } = string.Empty;
    public Guid InviteeId { get; set; }
    public string InviteeName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public bool IsExpired => Status == "Pending" && ExpiresAt <= DateTime.UtcNow;
}

public class CreateInvitationRequest
{
    public Guid InviteeId { get; set; }
    public string? Message { get; set; }
}

public class PlayerConnectionDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? UserBio { get; set; }
    public string? AvatarUrl { get; set; }
    public string SkillLevel { get; set; } = "Beginner";
    public string? PreferredSport { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsInitiator { get; set; }
}

public class SendConnectionRequest
{
    public Guid TargetUserId { get; set; }
}

public class BlockUserRequest
{
    public Guid TargetUserId { get; set; }
}

public class GameHistoryItemDto
{
    public Guid GameId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string SportType { get; set; } = string.Empty;
    public string VenueName { get; set; } = string.Empty;
    public string CourtName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Team { get; set; }
    public bool IsCreator { get; set; }
    public decimal PricePaid { get; set; }
    public int TotalPlayers { get; set; }
    public DateTime JoinedAt { get; set; }
}

public class GameHistoryFilterRequest : PagedRequest
{
    public string? SportType { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public string? Status { get; set; }
}

public class PlayerReputationDto
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public double ReliabilityScore { get; set; }
    public int CompletedMatchesCount { get; set; }
    public int OrganizedMatchesCount { get; set; }
    public int CancelledMatchesCount { get; set; }
    public int TotalJoinedMatches { get; set; }
    public double AttendanceRate { get; set; }
    public string ReliabilityTier { get; set; } = "Building History";
    public List<PlayerSportSkillDto> SportBreakdown { get; set; } = [];
    public string Explanation { get; set; } = string.Empty;
}

public class PublicPlayerProfileDto
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }
    public string SkillLevel { get; set; } = "Beginner";
    public string? PreferredSport { get; set; }
    public List<string> PreferredCities { get; set; } = [];
    public DateTime MemberSince { get; set; }
    public PlayerReputationDto Reputation { get; set; } = new();
    public string ConnectionStatusWithCaller { get; set; } = "None"; // None, PendingSent, PendingReceived, Connected, BlockedByCaller, BlockedByTarget
}

public class SearchPlayersRequest : PagedRequest
{
    public string? Query { get; set; }
    public string? SportType { get; set; }
    public string? City { get; set; }
}

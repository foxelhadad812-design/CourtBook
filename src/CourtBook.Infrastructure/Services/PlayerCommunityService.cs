using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CourtBook.Infrastructure.Services;

public class PlayerCommunityService : IPlayerCommunityService
{
    private readonly AppDbContext _db;
    private readonly ILogger<PlayerCommunityService> _logger;

    public PlayerCommunityService(AppDbContext db, ILogger<PlayerCommunityService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Result<PagedResult<GameHistoryItemDto>>> GetPlayerGameHistoryAsync(
        Guid callerId,
        Guid targetUserId,
        GameHistoryFilterRequest request)
    {
        var targetUser = await _db.Users.FindAsync(targetUserId);
        if (targetUser is null || !targetUser.IsActive)
            return Error.NotFound("Player not found.");

        if (callerId != targetUserId)
        {
            bool isBlocked = await _db.PlayerConnections.AnyAsync(c =>
                c.Status == ConnectionStatus.Blocked &&
                ((c.RequesterId == callerId && c.AddresseeId == targetUserId) ||
                 (c.RequesterId == targetUserId && c.AddresseeId == callerId)));

            if (isBlocked)
                return Error.Forbidden("Cannot access player game history due to privacy or block status.");
        }

        var nowEgypt = TimeZoneHelper.ConvertUtcToEgypt(DateTime.UtcNow);
        var today = DateOnly.FromDateTime(nowEgypt);
        var nowTime = TimeOnly.FromDateTime(nowEgypt);

        // History includes games that are completed or started in the past
        var query = _db.GameParticipants
            .AsNoTracking()
            .Include(p => p.Game).ThenInclude(g => g.Venue)
            .Include(p => p.Game).ThenInclude(g => g.Court)
            .Include(p => p.Game).ThenInclude(g => g.Participants)
            .Where(p => p.UserId == targetUserId)
            .Where(p => p.Game.Date < today || (p.Game.Date == today && p.Game.StartTime <= nowTime) || p.Game.Status == GameStatus.Completed);

        // If caller is not the target user, hide private matches where caller was not a participant
        if (callerId != targetUserId)
        {
            query = query.Where(p => !p.Game.IsPrivate || p.Game.Participants.Any(part => part.UserId == callerId));
        }

        if (!string.IsNullOrWhiteSpace(request.SportType) && Enum.TryParse<SportType>(request.SportType, true, out var sport))
        {
            query = query.Where(p => p.Game.SportType == sport);
        }

        if (request.FromDate.HasValue)
        {
            query = query.Where(p => p.Game.Date >= request.FromDate.Value);
        }

        if (request.ToDate.HasValue)
        {
            query = query.Where(p => p.Game.Date <= request.ToDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Status) && Enum.TryParse<GameStatus>(request.Status, true, out var status))
        {
            query = query.Where(p => p.Game.Status == status);
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(p => p.Game.Date)
            .ThenByDescending(p => p.Game.StartTime)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(p => new GameHistoryItemDto
            {
                GameId = p.GameId,
                Title = p.Game.Title,
                SportType = p.Game.SportType.ToString(),
                VenueName = p.Game.Venue != null ? p.Game.Venue.Name : "Venue",
                CourtName = p.Game.Court != null ? p.Game.Court.Name : "Court",
                City = p.Game.Venue != null ? p.Game.Venue.City : "Cairo",
                Date = p.Game.Date,
                StartTime = p.Game.StartTime.ToString("HH:mm"),
                EndTime = p.Game.EndTime.ToString("HH:mm"),
                Status = p.Game.Status.ToString(),
                Team = p.Team,
                IsCreator = p.Game.CreatorId == targetUserId,
                PricePaid = p.Game.PricePerPlayer,
                TotalPlayers = p.Game.Participants.Count,
                JoinedAt = p.JoinedAt
            })
            .ToListAsync();

        return PagedResult<GameHistoryItemDto>.From(items, total, request.Page, request.PageSize);
    }

    public async Task<Result<PlayerReputationDto>> GetPlayerReputationAsync(Guid userId)
    {
        var user = await _db.Users
            .Include(u => u.SportSkills)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user is null)
            return Error.NotFound("Player not found.");

        var nowEgypt = TimeZoneHelper.ConvertUtcToEgypt(DateTime.UtcNow);
        var today = DateOnly.FromDateTime(nowEgypt);
        var nowTime = TimeOnly.FromDateTime(nowEgypt);

        // Participations in past matches
        var pastParticipations = await _db.GameParticipants
            .AsNoTracking()
            .Include(p => p.Game)
            .Where(p => p.UserId == userId)
            .Where(p => p.Game.Date < today || (p.Game.Date == today && p.Game.StartTime <= nowTime) || p.Game.Status == GameStatus.Completed)
            .ToListAsync();

        int totalJoined = pastParticipations.Count;
        int completedMatches = pastParticipations.Count(p => p.Game.Status != GameStatus.Cancelled);

        // Matches created/organized by player
        var organizedGames = await _db.Games
            .AsNoTracking()
            .Where(g => g.CreatorId == userId)
            .Where(g => g.Date < today || (g.Date == today && g.StartTime <= nowTime) || g.Status == GameStatus.Completed || g.Status == GameStatus.Cancelled)
            .ToListAsync();

        int organizedMatches = organizedGames.Count;
        int cancelledMatches = organizedGames.Count(g => g.Status == GameStatus.Cancelled);

        double attendanceRate = totalJoined > 0
            ? Math.Round(100.0 * completedMatches / totalJoined, 1)
            : 100.0;

        // Calculate explainable reliability score (0 - 100%)
        double reliabilityScore;
        string tier;
        string explanation;

        if (totalJoined == 0 && organizedMatches == 0)
        {
            reliabilityScore = 100.0;
            tier = "Building History";
            explanation = "New player profile. Default maximum reliability rating assigned until match history is recorded.";
        }
        else
        {
            // Cancellation penalty: up to 30% reduction if organized matches are frequently cancelled
            double cancellationPenalty = organizedMatches > 0
                ? Math.Min(30.0, 30.0 * ((double)cancelledMatches / organizedMatches))
                : 0.0;

            reliabilityScore = Math.Round(Math.Clamp(attendanceRate - cancellationPenalty, 0.0, 100.0), 1);

            tier = reliabilityScore switch
            {
                >= 90.0 => "Elite / Very Reliable",
                >= 75.0 => "Reliable",
                >= 50.0 => "Fair",
                _ => "Needs Improvement"
            };

            explanation = $"Based on {completedMatches} completed match(es) ({attendanceRate}% attendance) and {organizedMatches} organized match(es) with {cancelledMatches} cancellation(s).";
        }

        var sportSkills = user.SportSkills.Select(s => new PlayerSportSkillDto
        {
            Id = s.Id,
            SportType = s.SportType.ToString(),
            SkillLevel = s.SkillLevel.ToString(),
            SkillScore = s.SkillScore,
            MatchesPlayed = s.MatchesPlayed,
            UpdatedAt = s.UpdatedAt
        }).ToList();

        return new PlayerReputationDto
        {
            UserId = userId,
            UserName = user.Name,
            ReliabilityScore = reliabilityScore,
            CompletedMatchesCount = completedMatches,
            OrganizedMatchesCount = organizedMatches,
            CancelledMatchesCount = cancelledMatches,
            TotalJoinedMatches = totalJoined,
            AttendanceRate = attendanceRate,
            ReliabilityTier = tier,
            SportBreakdown = sportSkills,
            Explanation = explanation
        };
    }

    public async Task<Result<PublicPlayerProfileDto>> GetPublicPlayerProfileAsync(Guid? callerId, Guid targetUserId)
    {
        var user = await _db.Users
            .Include(u => u.Profile)
            .Include(u => u.Preference)
            .Include(u => u.SportSkills)
            .FirstOrDefaultAsync(u => u.Id == targetUserId);

        if (user is null || !user.IsActive)
            return Error.NotFound("Player profile not found or inactive.");

        string connectionStatus = "None";

        if (callerId.HasValue && callerId.Value != targetUserId)
        {
            var forward = await _db.PlayerConnections
                .FirstOrDefaultAsync(c => c.RequesterId == callerId.Value && c.AddresseeId == targetUserId);

            var reverse = await _db.PlayerConnections
                .FirstOrDefaultAsync(c => c.RequesterId == targetUserId && c.AddresseeId == callerId.Value);

            if (forward?.Status == ConnectionStatus.Blocked)
                connectionStatus = "BlockedByCaller";
            else if (reverse?.Status == ConnectionStatus.Blocked)
                connectionStatus = "BlockedByTarget";
            else if (forward?.Status == ConnectionStatus.Accepted || reverse?.Status == ConnectionStatus.Accepted)
                connectionStatus = "Connected";
            else if (forward?.Status == ConnectionStatus.Pending)
                connectionStatus = "PendingSent";
            else if (reverse?.Status == ConnectionStatus.Pending)
                connectionStatus = "PendingReceived";
        }
        else if (callerId.HasValue && callerId.Value == targetUserId)
        {
            connectionStatus = "Self";
        }

        var repResult = await GetPlayerReputationAsync(targetUserId);
        var rep = repResult.IsSuccess ? repResult.Value : new PlayerReputationDto { UserId = targetUserId, UserName = user.Name };

        var cities = user.Preference?.PreferredCities?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList() ?? [];

        return new PublicPlayerProfileDto
        {
            UserId = user.Id,
            Name = user.Name,
            AvatarUrl = user.Profile?.AvatarUrl,
            Bio = user.Profile?.Bio,
            SkillLevel = user.Profile?.SkillLevel.ToString() ?? "Beginner",
            PreferredSport = user.Profile?.PreferredSport?.ToString(),
            PreferredCities = cities,
            MemberSince = user.CreatedAt,
            Reputation = rep,
            ConnectionStatusWithCaller = connectionStatus
        };
    }

    public async Task<PagedResult<PublicPlayerProfileDto>> SearchPlayersAsync(Guid callerId, SearchPlayersRequest request)
    {
        // Get list of users blocked by caller or who blocked caller
        var blockedUserIds = await _db.PlayerConnections
            .AsNoTracking()
            .Where(c => c.Status == ConnectionStatus.Blocked && (c.RequesterId == callerId || c.AddresseeId == callerId))
            .Select(c => c.RequesterId == callerId ? c.AddresseeId : c.RequesterId)
            .ToListAsync();

        var query = _db.Users
            .AsNoTracking()
            .Include(u => u.Profile)
            .Include(u => u.Preference)
            .Include(u => u.SportSkills)
            .Where(u => u.IsActive && u.Id != callerId && !blockedUserIds.Contains(u.Id));

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var q = request.Query.Trim().ToLower();
            query = query.Where(u => u.Name.ToLower().Contains(q) || (u.Profile != null && u.Profile.Bio != null && u.Profile.Bio.ToLower().Contains(q)));
        }

        if (!string.IsNullOrWhiteSpace(request.SportType) && Enum.TryParse<SportType>(request.SportType, true, out var sport))
        {
            query = query.Where(u => (u.Profile != null && u.Profile.PreferredSport == sport) ||
                                     u.SportSkills.Any(s => s.SportType == sport) ||
                                     (u.Preference != null && u.Preference.PreferredSports.Contains(request.SportType)));
        }

        if (!string.IsNullOrWhiteSpace(request.City))
        {
            var city = request.City.Trim().ToLower();
            query = query.Where(u => u.Preference != null && u.Preference.PreferredCities.ToLower().Contains(city));
        }

        var total = await query.CountAsync();
        var users = await query
            .OrderBy(u => u.Name)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToListAsync();

        var dtos = new List<PublicPlayerProfileDto>();
        foreach (var u in users)
        {
            var profileResult = await GetPublicPlayerProfileAsync(callerId, u.Id);
            if (profileResult.IsSuccess)
            {
                dtos.Add(profileResult.Value);
            }
        }

        return PagedResult<PublicPlayerProfileDto>.From(dtos, total, request.Page, request.PageSize);
    }
}

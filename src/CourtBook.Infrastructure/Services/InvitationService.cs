using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CourtBook.Infrastructure.Services;

public class InvitationService : IInvitationService
{
    private readonly AppDbContext _db;
    private readonly IGameService _gameService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<InvitationService> _logger;

    public InvitationService(
        AppDbContext db,
        IGameService gameService,
        INotificationService notificationService,
        ILogger<InvitationService> logger)
    {
        _db = db;
        _gameService = gameService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<Result<GameInvitationDto>> CreateInvitationAsync(Guid inviterId, Guid gameId, CreateInvitationRequest request)
    {
        if (request.InviteeId == inviterId)
            return Error.BadRequest("You cannot invite yourself to a game.");

        var inviter = await _db.Users.FindAsync(inviterId);
        if (inviter is null)
            return Error.NotFound("Inviter not found.");

        var invitee = await _db.Users
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Id == request.InviteeId);

        if (invitee is null || !invitee.IsActive)
            return Error.NotFound("Invited player not found or is inactive.");

        // Check if there is an active block relationship between the users
        bool isBlocked = await _db.PlayerConnections.AnyAsync(c =>
            c.Status == ConnectionStatus.Blocked &&
            ((c.RequesterId == inviterId && c.AddresseeId == request.InviteeId) ||
             (c.RequesterId == request.InviteeId && c.AddresseeId == inviterId)));

        if (isBlocked)
            return Error.Forbidden("Cannot invite this player due to a blocked relationship.");

        var game = await _db.Games
            .Include(g => g.Participants)
            .Include(g => g.Venue)
            .FirstOrDefaultAsync(g => g.Id == gameId);

        if (game is null)
            return Error.NotFound("Game not found.");

        bool isCreator = game.CreatorId == inviterId;
        bool isParticipant = game.Participants.Any(p => p.UserId == inviterId);

        if (!isCreator && !isParticipant)
            return Error.Forbidden("Only game organizers or confirmed participants can invite players.");

        if (game.Status != GameStatus.Open)
            return Error.BadRequest($"Cannot send invitation. Game is {game.Status}.");

        var gameStartUtc = TimeZoneHelper.CreateUtcFromEgyptDateAndTime(game.Date, game.StartTime);
        if (gameStartUtc <= DateTime.UtcNow)
            return Error.BadRequest("Cannot invite players to a match that has already started.");

        if (game.Participants.Count >= game.MaxPlayers)
            return Error.BadRequest("Game has reached maximum player capacity.");

        if (game.Participants.Any(p => p.UserId == request.InviteeId))
            return Error.Conflict("Player has already joined this game.");

        if (invitee.DateOfBirth.HasValue && !IsAgeEligible(game, invitee.DateOfBirth.Value))
            return Error.BadRequest("Player does not meet the age requirements for this game.");

        // Prevent duplicate active invitations
        var nowUtc = DateTime.UtcNow;
        var existingPending = await _db.GameInvitations.FirstOrDefaultAsync(i =>
            i.GameId == gameId &&
            i.InviteeId == request.InviteeId &&
            i.Status == InvitationStatus.Pending &&
            i.ExpiresAt > nowUtc);

        if (existingPending is not null)
            return Error.Conflict("An active invitation already exists for this player and game.");

        // Default expiration: 24 hours or game start, whichever is sooner
        var expiresAt = nowUtc.AddHours(24);
        if (expiresAt > gameStartUtc)
            expiresAt = gameStartUtc;

        var invitation = new GameInvitation
        {
            Id = Guid.NewGuid(),
            GameId = gameId,
            InviterId = inviterId,
            InviteeId = request.InviteeId,
            Status = InvitationStatus.Pending,
            Message = request.Message?.Trim(),
            ExpiresAt = expiresAt,
            CreatedAt = nowUtc,
            ConcurrencyStamp = Guid.NewGuid()
        };

        _db.GameInvitations.Add(invitation);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Invitation {InvitationId} created by User {InviterId} for User {InviteeId} to Game {GameId}",
            invitation.Id, inviterId, request.InviteeId, gameId);

        // Send real-time community notification
        await _notificationService.SendNotificationAsync(
            request.InviteeId,
            "Game Invitation",
            $"{inviter.Name} invited you to play {game.SportType} at {game.Venue.Name} on {game.Date:yyyy-MM-dd} at {game.StartTime:HH\\:mm}.",
            NotificationType.GameInvite,
            $"/Games?invitationId={invitation.Id}");

        return MapToDto(invitation, game, inviter, invitee);
    }

    public async Task<PagedResult<GameInvitationDto>> GetReceivedInvitationsAsync(Guid userId, PagedRequest request)
    {
        var query = _db.GameInvitations
            .AsNoTracking()
            .Include(i => i.Game).ThenInclude(g => g.Venue)
            .Include(i => i.Inviter)
            .Include(i => i.Invitee)
            .Where(i => i.InviteeId == userId)
            .OrderByDescending(i => i.CreatedAt);

        var total = await query.CountAsync();
        var items = await query
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(i => MapToDto(i, i.Game, i.Inviter, i.Invitee))
            .ToListAsync();

        return PagedResult<GameInvitationDto>.From(items, total, request.Page, request.PageSize);
    }

    public async Task<PagedResult<GameInvitationDto>> GetSentInvitationsAsync(Guid userId, PagedRequest request)
    {
        var query = _db.GameInvitations
            .AsNoTracking()
            .Include(i => i.Game).ThenInclude(g => g.Venue)
            .Include(i => i.Inviter)
            .Include(i => i.Invitee)
            .Where(i => i.InviterId == userId)
            .OrderByDescending(i => i.CreatedAt);

        var total = await query.CountAsync();
        var items = await query
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(i => MapToDto(i, i.Game, i.Inviter, i.Invitee))
            .ToListAsync();

        return PagedResult<GameInvitationDto>.From(items, total, request.Page, request.PageSize);
    }

    public async Task<Result<GameResponse>> AcceptInvitationAsync(Guid userId, Guid invitationId)
    {
        var invitation = await _db.GameInvitations
            .Include(i => i.Game).ThenInclude(g => g.Venue)
            .Include(i => i.Inviter)
            .Include(i => i.Invitee)
            .FirstOrDefaultAsync(i => i.Id == invitationId);

        if (invitation is null)
            return Error.NotFound("Invitation not found.");

        if (invitation.InviteeId != userId)
            return Error.Forbidden("You are not authorized to accept this invitation.");

        if (invitation.Status != InvitationStatus.Pending)
            return Error.BadRequest($"Cannot accept invitation. Current status is {invitation.Status}.");

        if (invitation.ExpiresAt <= DateTime.UtcNow)
        {
            invitation.Status = InvitationStatus.Expired;
            invitation.ConcurrencyStamp = Guid.NewGuid();
            await _db.SaveChangesAsync();
            return Error.BadRequest("This game invitation has expired.");
        }

        // CRITICAL INTEGRATION: Reuse GameService JoinGameAsync to guarantee exact capacity,
        // optimistic concurrency tokens, and duplicate prevention invariants.
        var joinResult = await _gameService.JoinGameAsync(userId, invitation.GameId, invitation.Game.AccessCode);
        if (joinResult.IsFailure)
        {
            return joinResult.Error;
        }

        // Mark invitation as Accepted
        invitation.Status = InvitationStatus.Accepted;
        invitation.RespondedAt = DateTime.UtcNow;
        invitation.ConcurrencyStamp = Guid.NewGuid();
        await _db.SaveChangesAsync();

        _logger.LogInformation("Invitation {InvitationId} accepted by User {UserId} for Game {GameId}",
            invitation.Id, userId, invitation.GameId);

        // Notify Inviter and Game Creator
        await _notificationService.SendNotificationAsync(
            invitation.InviterId,
            "Invitation Accepted",
            $"{invitation.Invitee.Name} accepted your invitation to {invitation.Game.Title}.",
            NotificationType.GameInviteAccepted,
            $"/Games/Lobby?gameId={invitation.GameId}");

        if (invitation.Game.CreatorId != invitation.InviterId)
        {
            await _notificationService.SendNotificationAsync(
                invitation.Game.CreatorId,
                "Player Joined via Invitation",
                $"{invitation.Invitee.Name} joined your match {invitation.Game.Title} via invitation.",
                NotificationType.GameJoined,
                $"/Games/Lobby?gameId={invitation.GameId}");
        }

        var gameResult = await _gameService.GetByIdAsync(invitation.GameId, userId);
        return gameResult;
    }

    public async Task<Result> DeclineInvitationAsync(Guid userId, Guid invitationId)
    {
        var invitation = await _db.GameInvitations
            .Include(i => i.Game)
            .Include(i => i.Inviter)
            .Include(i => i.Invitee)
            .FirstOrDefaultAsync(i => i.Id == invitationId);

        if (invitation is null)
            return Error.NotFound("Invitation not found.");

        if (invitation.InviteeId != userId)
            return Error.Forbidden("You are not authorized to decline this invitation.");

        if (invitation.Status != InvitationStatus.Pending)
            return Error.BadRequest($"Cannot decline invitation. Current status is {invitation.Status}.");

        invitation.Status = InvitationStatus.Declined;
        invitation.RespondedAt = DateTime.UtcNow;
        invitation.ConcurrencyStamp = Guid.NewGuid();
        await _db.SaveChangesAsync();

        _logger.LogInformation("Invitation {InvitationId} declined by User {UserId}", invitation.Id, userId);

        await _notificationService.SendNotificationAsync(
            invitation.InviterId,
            "Invitation Declined",
            $"{invitation.Invitee.Name} declined the invitation to {invitation.Game.Title}.",
            NotificationType.GameInviteDeclined);

        return Result.Ok();
    }

    public async Task<Result> CancelInvitationAsync(Guid userId, Guid invitationId)
    {
        var invitation = await _db.GameInvitations
            .Include(i => i.Game)
            .FirstOrDefaultAsync(i => i.Id == invitationId);

        if (invitation is null)
            return Error.NotFound("Invitation not found.");

        if (invitation.InviterId != userId && invitation.Game.CreatorId != userId)
            return Error.Forbidden("Only the inviter or game creator can cancel this invitation.");

        if (invitation.Status != InvitationStatus.Pending)
            return Error.BadRequest($"Cannot cancel invitation. Current status is {invitation.Status}.");

        invitation.Status = InvitationStatus.Cancelled;
        invitation.RespondedAt = DateTime.UtcNow;
        invitation.ConcurrencyStamp = Guid.NewGuid();
        await _db.SaveChangesAsync();

        _logger.LogInformation("Invitation {InvitationId} cancelled by User {UserId}", invitation.Id, userId);

        return Result.Ok();
    }

    private static bool IsAgeEligible(Game game, DateOnly dateOfBirth)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        int age = today.Year - dateOfBirth.Year;
        if (dateOfBirth > today.AddYears(-age)) age--;

        return game.AgeGroup switch
        {
            AgeGroup.Kids => age >= 6 && age <= 12,
            AgeGroup.Juniors => age >= 13 && age <= 17,
            AgeGroup.Teens => age >= 16 && age <= 19,
            AgeGroup.Adults => age >= 18,
            AgeGroup.Custom => (!game.MinAge.HasValue || age >= game.MinAge.Value) &&
                               (!game.MaxAge.HasValue || age <= game.MaxAge.Value),
            _ => true
        };
    }

    private static GameInvitationDto MapToDto(GameInvitation i, Game g, User inviter, User invitee)
    {
        return new GameInvitationDto
        {
            Id = i.Id,
            GameId = i.GameId,
            GameTitle = g?.Title ?? "Match",
            SportType = g?.SportType.ToString() ?? "Sport",
            GameDate = g?.Date ?? DateOnly.MinValue,
            StartTime = g?.StartTime.ToString("HH:mm") ?? "00:00",
            EndTime = g?.EndTime.ToString("HH:mm") ?? "00:00",
            VenueName = g?.Venue?.Name ?? "Venue",
            City = g?.Venue?.City ?? "Cairo",
            PricePerPlayer = g?.PricePerPlayer ?? 0,
            IsPrivate = g?.IsPrivate ?? false,
            InviterId = i.InviterId,
            InviterName = inviter?.Name ?? "Player",
            InviteeId = i.InviteeId,
            InviteeName = invitee?.Name ?? "Player",
            Status = i.Status.ToString(),
            Message = i.Message,
            ExpiresAt = i.ExpiresAt,
            CreatedAt = i.CreatedAt,
            RespondedAt = i.RespondedAt
        };
    }
}

using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CourtBook.Infrastructure.Services;

public class ConnectionService : IConnectionService
{
    private readonly AppDbContext _db;
    private readonly INotificationService _notificationService;
    private readonly ILogger<ConnectionService> _logger;

    public ConnectionService(
        AppDbContext db,
        INotificationService notificationService,
        ILogger<ConnectionService> logger)
    {
        _db = db;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<Result<PlayerConnectionDto>> SendConnectionRequestAsync(Guid requesterId, Guid targetUserId)
    {
        if (requesterId == targetUserId)
            return Error.BadRequest("You cannot connect with yourself.");

        var requester = await _db.Users
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Id == requesterId);

        if (requester is null)
            return Error.NotFound("Requester not found.");

        var target = await _db.Users
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Id == targetUserId);

        if (target is null || !target.IsActive)
            return Error.NotFound("Player not found or is inactive.");

        var forward = await _db.PlayerConnections
            .Include(c => c.Requester).ThenInclude(u => u.Profile)
            .Include(c => c.Addressee).ThenInclude(u => u.Profile)
            .FirstOrDefaultAsync(c => c.RequesterId == requesterId && c.AddresseeId == targetUserId);

        var reverse = await _db.PlayerConnections
            .Include(c => c.Requester).ThenInclude(u => u.Profile)
            .Include(c => c.Addressee).ThenInclude(u => u.Profile)
            .FirstOrDefaultAsync(c => c.RequesterId == targetUserId && c.AddresseeId == requesterId);

        if (reverse?.Status == ConnectionStatus.Blocked)
            return Error.Forbidden("Cannot connect with this player.");

        if (forward?.Status == ConnectionStatus.Blocked)
            return Error.BadRequest("You have blocked this player. Unblock them first to connect.");

        // If target had already sent a pending request to requester -> auto-accept
        if (reverse?.Status == ConnectionStatus.Pending)
        {
            reverse.Status = ConnectionStatus.Accepted;
            reverse.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogInformation("Mutual connection request between {UserA} and {UserB} automatically accepted", requesterId, targetUserId);

            await _notificationService.SendNotificationAsync(
                targetUserId,
                "Connection Accepted",
                $"{requester.Name} accepted your connection request.",
                NotificationType.ConnectionAccepted,
                $"/Profile/Public?userId={requesterId}");

            return MapToDto(reverse, requesterId);
        }

        if (forward?.Status == ConnectionStatus.Accepted || reverse?.Status == ConnectionStatus.Accepted)
            return Error.Conflict("You are already connected with this player.");

        if (forward?.Status == ConnectionStatus.Pending)
            return Error.Conflict("A pending connection request has already been sent to this player.");

        if (forward?.Status == ConnectionStatus.Declined)
        {
            // Reactivate declined request
            forward.Status = ConnectionStatus.Pending;
            forward.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await _notificationService.SendNotificationAsync(
                targetUserId,
                "New Connection Request",
                $"{requester.Name} sent you a connection request.",
                NotificationType.ConnectionRequest,
                "/Community?tab=connections");

            return MapToDto(forward, requesterId);
        }

        var newConnection = new PlayerConnection
        {
            Id = Guid.NewGuid(),
            RequesterId = requesterId,
            AddresseeId = targetUserId,
            Status = ConnectionStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _db.PlayerConnections.Add(newConnection);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Connection request {ConnectionId} sent from {RequesterId} to {TargetUserId}",
            newConnection.Id, requesterId, targetUserId);

        await _notificationService.SendNotificationAsync(
            targetUserId,
            "New Connection Request",
            $"{requester.Name} sent you a connection request.",
            NotificationType.ConnectionRequest,
            "/Community?tab=connections");

        newConnection.Requester = requester;
        newConnection.Addressee = target;
        return MapToDto(newConnection, requesterId);
    }

    public async Task<Result<PlayerConnectionDto>> AcceptConnectionRequestAsync(Guid userId, Guid connectionId)
    {
        var conn = await _db.PlayerConnections
            .Include(c => c.Requester).ThenInclude(u => u.Profile)
            .Include(c => c.Addressee).ThenInclude(u => u.Profile)
            .FirstOrDefaultAsync(c => c.Id == connectionId);

        if (conn is null)
            return Error.NotFound("Connection request not found.");

        if (conn.AddresseeId != userId)
            return Error.Forbidden("You are not authorized to accept this connection request.");

        if (conn.Status != ConnectionStatus.Pending)
            return Error.BadRequest($"Cannot accept connection request. Current status is {conn.Status}.");

        conn.Status = ConnectionStatus.Accepted;
        conn.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Connection request {ConnectionId} accepted by {UserId}", connectionId, userId);

        await _notificationService.SendNotificationAsync(
            conn.RequesterId,
            "Connection Request Accepted",
            $"{conn.Addressee.Name} accepted your connection request.",
            NotificationType.ConnectionAccepted,
            $"/Profile/Public?userId={conn.AddresseeId}");

        return MapToDto(conn, userId);
    }

    public async Task<Result> DeclineConnectionRequestAsync(Guid userId, Guid connectionId)
    {
        var conn = await _db.PlayerConnections.FirstOrDefaultAsync(c => c.Id == connectionId);
        if (conn is null)
            return Error.NotFound("Connection request not found.");

        if (conn.AddresseeId != userId)
            return Error.Forbidden("You are not authorized to decline this connection request.");

        if (conn.Status != ConnectionStatus.Pending)
            return Error.BadRequest($"Cannot decline connection request. Current status is {conn.Status}.");

        conn.Status = ConnectionStatus.Declined;
        conn.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Connection request {ConnectionId} declined by {UserId}", connectionId, userId);

        return Result.Ok();
    }

    public async Task<Result> CancelOrRemoveConnectionAsync(Guid userId, Guid targetUserIdOrConnectionId)
    {
        var conn = await _db.PlayerConnections.FirstOrDefaultAsync(c =>
            c.Id == targetUserIdOrConnectionId ||
            (c.RequesterId == userId && c.AddresseeId == targetUserIdOrConnectionId) ||
            (c.AddresseeId == userId && c.RequesterId == targetUserIdOrConnectionId));

        if (conn is null)
            return Error.NotFound("Connection not found.");

        if (conn.RequesterId != userId && conn.AddresseeId != userId)
            return Error.Forbidden("You are not authorized to remove this connection.");

        _db.PlayerConnections.Remove(conn);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Connection {ConnectionId} removed by User {UserId}", conn.Id, userId);
        return Result.Ok();
    }

    public async Task<Result> BlockUserAsync(Guid callerId, Guid targetUserId)
    {
        if (callerId == targetUserId)
            return Error.BadRequest("You cannot block yourself.");

        var target = await _db.Users.FindAsync(targetUserId);
        if (target is null)
            return Error.NotFound("Player not found.");

        var existingForward = await _db.PlayerConnections
            .FirstOrDefaultAsync(c => c.RequesterId == callerId && c.AddresseeId == targetUserId);

        var existingReverse = await _db.PlayerConnections
            .FirstOrDefaultAsync(c => c.RequesterId == targetUserId && c.AddresseeId == callerId);

        if (existingReverse is not null)
        {
            _db.PlayerConnections.Remove(existingReverse);
        }

        if (existingForward is not null)
        {
            existingForward.Status = ConnectionStatus.Blocked;
            existingForward.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var blockConn = new PlayerConnection
            {
                Id = Guid.NewGuid(),
                RequesterId = callerId,
                AddresseeId = targetUserId,
                Status = ConnectionStatus.Blocked,
                CreatedAt = DateTime.UtcNow
            };
            _db.PlayerConnections.Add(blockConn);
        }

        // Cancel any pending game invitations between both users
        var pendingInvitations = await _db.GameInvitations
            .Where(i => i.Status == InvitationStatus.Pending &&
                        ((i.InviterId == callerId && i.InviteeId == targetUserId) ||
                         (i.InviterId == targetUserId && i.InviteeId == callerId)))
            .ToListAsync();

        foreach (var inv in pendingInvitations)
        {
            inv.Status = InvitationStatus.Cancelled;
            inv.RespondedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("User {CallerId} blocked User {TargetUserId}", callerId, targetUserId);

        return Result.Ok();
    }

    public async Task<Result> UnblockUserAsync(Guid callerId, Guid targetUserId)
    {
        var blockConn = await _db.PlayerConnections
            .FirstOrDefaultAsync(c => c.RequesterId == callerId && c.AddresseeId == targetUserId && c.Status == ConnectionStatus.Blocked);

        if (blockConn is null)
            return Error.NotFound("Blocked relationship not found.");

        _db.PlayerConnections.Remove(blockConn);
        await _db.SaveChangesAsync();

        _logger.LogInformation("User {CallerId} unblocked User {TargetUserId}", callerId, targetUserId);
        return Result.Ok();
    }

    public async Task<PagedResult<PlayerConnectionDto>> GetConnectionsAsync(Guid userId, ConnectionStatus? status, PagedRequest request)
    {
        var targetStatus = status ?? ConnectionStatus.Accepted;

        var query = _db.PlayerConnections
            .AsNoTracking()
            .Include(c => c.Requester).ThenInclude(u => u.Profile)
            .Include(c => c.Addressee).ThenInclude(u => u.Profile)
            .Where(c => c.Status == targetStatus)
            .Where(c => c.RequesterId == userId || c.AddresseeId == userId)
            .OrderByDescending(c => c.CreatedAt);

        var total = await query.CountAsync();
        var items = await query
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(c => MapToDto(c, userId))
            .ToListAsync();

        return PagedResult<PlayerConnectionDto>.From(items, total, request.Page, request.PageSize);
    }

    public async Task<PagedResult<PlayerConnectionDto>> GetBlockedUsersAsync(Guid userId, PagedRequest request)
    {
        var query = _db.PlayerConnections
            .AsNoTracking()
            .Include(c => c.Addressee).ThenInclude(u => u.Profile)
            .Where(c => c.RequesterId == userId && c.Status == ConnectionStatus.Blocked)
            .OrderByDescending(c => c.CreatedAt);

        var total = await query.CountAsync();
        var items = await query
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(c => MapToDto(c, userId))
            .ToListAsync();

        return PagedResult<PlayerConnectionDto>.From(items, total, request.Page, request.PageSize);
    }

    private static PlayerConnectionDto MapToDto(PlayerConnection c, Guid callerId)
    {
        bool isCallerRequester = c.RequesterId == callerId;
        var otherUser = isCallerRequester ? c.Addressee : c.Requester;

        return new PlayerConnectionDto
        {
            Id = c.Id,
            UserId = otherUser?.Id ?? Guid.Empty,
            UserName = otherUser?.Name ?? "Player",
            UserBio = otherUser?.Profile?.Bio,
            AvatarUrl = otherUser?.Profile?.AvatarUrl,
            SkillLevel = otherUser?.Profile?.SkillLevel.ToString() ?? "Beginner",
            PreferredSport = otherUser?.Profile?.PreferredSport?.ToString(),
            Status = c.Status.ToString(),
            CreatedAt = c.CreatedAt,
            IsInitiator = isCallerRequester
        };
    }
}

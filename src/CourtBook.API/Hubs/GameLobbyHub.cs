using System.Security.Claims;
using CourtBook.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.API.Hubs;

/// <summary>
/// Dedicated real-time game lobby hub for matchmaking and live match coordination.
/// Requires JWT authentication and validates membership for private games.
/// </summary>
[Authorize]
public class GameLobbyHub : Hub
{
    private readonly AppDbContext _db;
    private readonly ILogger<GameLobbyHub> _logger;

    public GameLobbyHub(AppDbContext db, ILogger<GameLobbyHub> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Joins the caller's connection to the game's real-time lobby group.
    /// Strictly validates that the game exists and private game permissions are respected.
    /// </summary>
    public async Task JoinLobby(Guid gameId)
    {
        var userIdStr = Context.UserIdentifier
            ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Context.User?.FindFirst("sub")?.Value;

        if (!Guid.TryParse(userIdStr, out var userId))
        {
            _logger.LogWarning("Unauthorized connection {ConnectionId} attempted to join game lobby {GameId}", Context.ConnectionId, gameId);
            throw new HubException("Unauthorized: Invalid user identity.");
        }

        var game = await _db.Games
            .AsNoTracking()
            .Include(g => g.Participants)
            .FirstOrDefaultAsync(g => g.Id == gameId);

        if (game is null)
        {
            throw new HubException("Game not found.");
        }

        // For private games, verify the user is a confirmed participant or creator
        if (game.IsPrivate && game.CreatorId != userId && !game.Participants.Any(p => p.UserId == userId))
        {
            _logger.LogWarning("User {UserId} rejected from private game lobby {GameId}", userId, gameId);
            throw new HubException("Forbidden: You must join or organize this private game before accessing its lobby.");
        }

        var groupName = $"game:{gameId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.LogInformation("User {UserId} (conn: {ConnectionId}) joined lobby group {GroupName}", userId, Context.ConnectionId, groupName);
    }

    /// <summary>
    /// Leaves the game's real-time lobby group.
    /// </summary>
    public async Task LeaveLobby(Guid gameId)
    {
        var groupName = $"game:{gameId}";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        _logger.LogInformation("Connection {ConnectionId} left lobby group {GroupName}", Context.ConnectionId, groupName);
    }
}

using CourtBook.API.Hubs;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace CourtBook.API.Services;

/// <summary>
/// Implements IGameLobbySender using ASP.NET Core SignalR IHubContext for GameLobbyHub.
/// Dispatches real-time events to game:{gameId} groups.
/// </summary>
public class SignalRGameLobbySender : IGameLobbySender
{
    private readonly IHubContext<GameLobbyHub> _hubContext;
    private readonly ILogger<SignalRGameLobbySender> _logger;

    public SignalRGameLobbySender(
        IHubContext<GameLobbyHub> hubContext,
        ILogger<SignalRGameLobbySender> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task SendPlayerJoinedAsync(Guid gameId, GameParticipantDto participant, CancellationToken cancellationToken = default)
    {
        var groupName = $"game:{gameId}";
        try
        {
            await _hubContext.Clients.Group(groupName).SendAsync("PlayerJoined", participant, cancellationToken);
            _logger.LogInformation("SignalR PlayerJoined dispatched to group {GroupName}", groupName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch PlayerJoined to group {GroupName}", groupName);
        }
    }

    public async Task SendPlayerLeftAsync(Guid gameId, Guid userId, string userName, CancellationToken cancellationToken = default)
    {
        var groupName = $"game:{gameId}";
        try
        {
            await _hubContext.Clients.Group(groupName).SendAsync("PlayerLeft", new { userId, userName }, cancellationToken);
            _logger.LogInformation("SignalR PlayerLeft dispatched to group {GroupName}", groupName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch PlayerLeft to group {GroupName}", groupName);
        }
    }

    public async Task SendGameFullAsync(Guid gameId, CancellationToken cancellationToken = default)
    {
        var groupName = $"game:{gameId}";
        try
        {
            await _hubContext.Clients.Group(groupName).SendAsync("GameFull", new { gameId }, cancellationToken);
            _logger.LogInformation("SignalR GameFull dispatched to group {GroupName}", groupName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch GameFull to group {GroupName}", groupName);
        }
    }

    public async Task SendPlayerReadyAsync(Guid gameId, Guid userId, bool isReady, bool allPlayersReady, CancellationToken cancellationToken = default)
    {
        var groupName = $"game:{gameId}";
        try
        {
            await _hubContext.Clients.Group(groupName).SendAsync("PlayerReady", new { userId, isReady, allPlayersReady }, cancellationToken);
            _logger.LogInformation("SignalR PlayerReady dispatched to group {GroupName}", groupName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch PlayerReady to group {GroupName}", groupName);
        }
    }

    public async Task SendTeamsUpdatedAsync(Guid gameId, BalanceTeamsResponse teams, CancellationToken cancellationToken = default)
    {
        var groupName = $"game:{gameId}";
        try
        {
            await _hubContext.Clients.Group(groupName).SendAsync("TeamUpdated", teams, cancellationToken);
            _logger.LogInformation("SignalR TeamUpdated dispatched to group {GroupName}", groupName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch TeamUpdated to group {GroupName}", groupName);
        }
    }

    public async Task SendGameCancelledAsync(Guid gameId, CancellationToken cancellationToken = default)
    {
        var groupName = $"game:{gameId}";
        try
        {
            await _hubContext.Clients.Group(groupName).SendAsync("GameCancelled", new { gameId }, cancellationToken);
            _logger.LogInformation("SignalR GameCancelled dispatched to group {GroupName}", groupName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch GameCancelled to group {GroupName}", groupName);
        }
    }
}

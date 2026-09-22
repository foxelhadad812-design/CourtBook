using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CourtBook.API.Hubs;

/// <summary>
/// Secure real-time notification hub.
/// Requires authentication; clients automatically join their personal group user:{userId}.
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(ILogger<NotificationHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier 
                     ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? Context.User?.FindFirst("sub")?.Value;

        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
            _logger.LogInformation("Client {ConnectionId} connected and joined group user:{UserId}", Context.ConnectionId, userId);
        }
        else
        {
            _logger.LogWarning("Authenticated connection {ConnectionId} has no resolved userId claim.", Context.ConnectionId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier 
                     ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? Context.User?.FindFirst("sub")?.Value;

        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user:{userId}");
            _logger.LogInformation("Client {ConnectionId} disconnected from group user:{UserId}", Context.ConnectionId, userId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}

using CourtBook.API.Hubs;
using CourtBook.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace CourtBook.API.Services;

/// <summary>
/// Implements IRealTimeNotificationSender using ASP.NET Core SignalR IHubContext.
/// Dispatches real-time notification events to the target user's isolated group user:{userId}.
/// </summary>
public class SignalRNotificationSender : IRealTimeNotificationSender
{
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<SignalRNotificationSender> _logger;

    public SignalRNotificationSender(
        IHubContext<NotificationHub> hubContext,
        ILogger<SignalRNotificationSender> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task SendNotificationToUserAsync(Guid userId, object notificationPayload, CancellationToken cancellationToken = default)
    {
        var groupName = $"user:{userId}";
        try
        {
            await _hubContext.Clients.Group(groupName).SendAsync("ReceiveNotification", notificationPayload, cancellationToken);
            _logger.LogInformation("SignalR notification successfully pushed to group {GroupName}", groupName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push SignalR notification to group {GroupName}", groupName);
        }
    }
}

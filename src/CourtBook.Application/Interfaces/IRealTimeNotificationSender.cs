namespace CourtBook.Application.Interfaces;

/// <summary>
/// Abstraction for real-time notification push (e.g. via SignalR or WebSocket).
/// Decouples Application/Infrastructure from ASP.NET Core SignalR hub dependencies.
/// </summary>
public interface IRealTimeNotificationSender
{
    /// <summary>
    /// Delivers a real-time notification payload to all active client connections belonging to the specified user.
    /// </summary>
    Task SendNotificationToUserAsync(Guid userId, object notificationPayload, CancellationToken cancellationToken = default);
}

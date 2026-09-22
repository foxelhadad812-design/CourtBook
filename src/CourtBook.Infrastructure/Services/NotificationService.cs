using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CourtBook.Infrastructure.Services;

public class NotificationService : INotificationService
{
    private readonly AppDbContext _db;
    private readonly ILogger<NotificationService> _logger;
    private readonly IEmailSender? _emailSender;
    private readonly IPushNotificationSender? _pushSender;
    private readonly IRealTimeNotificationSender? _realTimeSender;

    public NotificationService(
        AppDbContext db,
        ILogger<NotificationService> logger,
        IEmailSender? emailSender = null,
        IPushNotificationSender? pushSender = null,
        IRealTimeNotificationSender? realTimeSender = null)
    {
        _db = db;
        _logger = logger;
        _emailSender = emailSender;
        _pushSender = pushSender;
        _realTimeSender = realTimeSender;
    }

    public async Task SendNotificationAsync(Guid userId, string title, string message, NotificationType type, string? actionUrl = null)
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title,
            Message = message,
            Type = type,
            ActionUrl = actionUrl,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Notification [{Type}] sent to User {UserId}: {Title}", type, userId, title);

        // Optional external channels
        if (_pushSender is not null)
        {
            try { await _pushSender.SendPushAsync(userId, title, message, actionUrl); }
            catch (Exception ex) { _logger.LogWarning(ex, "Push delivery failed for user {UserId}", userId); }
        }

        // Real-time delivery (SignalR)
        if (_realTimeSender is not null)
        {
            try
            {
                var payload = new
                {
                    id = notification.Id,
                    title = notification.Title,
                    message = notification.Message,
                    type = notification.Type.ToString(),
                    actionUrl = notification.ActionUrl,
                    createdAt = notification.CreatedAt,
                    isRead = false
                };
                await _realTimeSender.SendNotificationToUserAsync(userId, payload);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Real-time notification delivery failed for user {UserId}", userId);
            }
        }
    }

    public async Task<PagedResult<NotificationDto>> GetUserNotificationsAsync(Guid userId, PagedRequest request)
    {
        var query = _db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt);

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(n => new NotificationDto
            {
                Id = n.Id,
                Title = n.Title,
                Message = n.Message,
                Type = n.Type.ToString(),
                ActionUrl = n.ActionUrl,
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt
            })
            .ToListAsync();

        return PagedResult<NotificationDto>.From(items, totalCount, request.Page, request.PageSize);
    }

    public async Task<int> GetUnreadCountAsync(Guid userId)
    {
        return await _db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.UserId == userId && !n.IsRead);
    }

    public async Task<Result> MarkAsReadAsync(Guid userId, Guid notificationId)
    {
        var notification = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);
        if (notification is null)
            return Error.NotFound("Notification");

        notification.IsRead = true;
        notification.ReadAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Result.Ok();
    }

    public async Task<Result> MarkAllAsReadAsync(Guid userId)
    {
        var unread = await _db.Notifications.Where(n => n.UserId == userId && !n.IsRead).ToListAsync();
        foreach (var n in unread)
        {
            n.IsRead = true;
            n.ReadAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return Result.Ok();
    }
}

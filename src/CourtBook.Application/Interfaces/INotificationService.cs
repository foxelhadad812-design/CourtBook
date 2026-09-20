using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Domain.Enums;

namespace CourtBook.Application.Interfaces;

public interface INotificationService
{
    Task SendNotificationAsync(Guid userId, string title, string message, NotificationType type, string? actionUrl = null);
    Task<PagedResult<NotificationDto>> GetUserNotificationsAsync(Guid userId, PagedRequest request);
    Task<int> GetUnreadCountAsync(Guid userId);
    Task<Result> MarkAsReadAsync(Guid userId, Guid notificationId);
    Task<Result> MarkAllAsReadAsync(Guid userId);
}

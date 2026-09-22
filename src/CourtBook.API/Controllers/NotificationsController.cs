using CourtBook.API.Extensions;
using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CourtBook.API.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    /// <summary>
    /// Gets the current player's notifications with pagination.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<NotificationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNotifications([FromQuery] PagedRequest request)
    {
        var userId = User.GetUserId();
        var result = await _notificationService.GetUserNotificationsAsync(userId, request);
        return Ok(result);
    }

    /// <summary>
    /// Gets the count of unread notifications for badge display.
    /// </summary>
    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnreadCount()
    {
        var userId = User.GetUserId();
        var count = await _notificationService.GetUnreadCountAsync(userId);
        return Ok(new { unreadCount = count });
    }

    /// <summary>
    /// Marks a single notification as read.
    /// </summary>
    [HttpPut("{id}/read")]
    [HttpPost("{id}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkAsRead(Guid id)
    {
        var userId = User.GetUserId();
        var result = await _notificationService.MarkAsReadAsync(userId, id);
        return result.ToActionResult();
    }

    /// <summary>
    /// Marks all unread notifications as read.
    /// </summary>
    [HttpPut("read-all")]
    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var userId = User.GetUserId();
        var result = await _notificationService.MarkAllAsReadAsync(userId);
        return result.ToActionResult();
    }
}

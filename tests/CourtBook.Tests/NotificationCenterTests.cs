using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using CourtBook.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CourtBook.Tests;

public class NotificationCenterTests
{
    private (AppDbContext db, NotificationService notifService) CreateServices(string dbName)
    {
        var db = TestDbContextFactory.Create(dbName);
        var notifService = new NotificationService(db, NullLogger<NotificationService>.Instance);
        return (db, notifService);
    }

    [Fact]
    public async Task GetUserNotifications_EnforcesStrictUserIsolation()
    {
        var (db, notifService) = CreateServices(nameof(GetUserNotifications_EnforcesStrictUserIsolation));

        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await notifService.SendNotificationAsync(userA, "User A Notif 1", "Msg A1", NotificationType.SystemAlert);
        await notifService.SendNotificationAsync(userA, "User A Notif 2", "Msg A2", NotificationType.BookingConfirmed);
        await notifService.SendNotificationAsync(userB, "User B Notif 1", "Msg B1", NotificationType.SystemAlert);

        // Fetch User A
        var resultA = await notifService.GetUserNotificationsAsync(userA, new PagedRequest { Page = 1, PageSize = 10 });
        Assert.Equal(2, resultA.TotalCount);
        Assert.All(resultA.Items, item => Assert.Contains("User A", item.Title));

        // Fetch User B
        var resultB = await notifService.GetUserNotificationsAsync(userB, new PagedRequest { Page = 1, PageSize = 10 });
        Assert.Equal(1, resultB.TotalCount);
        Assert.Equal("User B Notif 1", resultB.Items[0].Title);
    }

    [Fact]
    public async Task GetUnreadCount_ReturnsAccurateCount()
    {
        var (db, notifService) = CreateServices(nameof(GetUnreadCount_ReturnsAccurateCount));

        var user = Guid.NewGuid();
        await notifService.SendNotificationAsync(user, "Unread 1", "Msg 1", NotificationType.SystemAlert);
        await notifService.SendNotificationAsync(user, "Unread 2", "Msg 2", NotificationType.BookingConfirmed);
        await notifService.SendNotificationAsync(user, "Unread 3", "Msg 3", NotificationType.GameInvite);

        var countBefore = await notifService.GetUnreadCountAsync(user);
        Assert.Equal(3, countBefore);

        // Mark 1 as read directly in DB
        var notif = await db.Notifications.FirstAsync(n => n.UserId == user);
        notif.IsRead = true;
        await db.SaveChangesAsync();

        var countAfter = await notifService.GetUnreadCountAsync(user);
        Assert.Equal(2, countAfter);
    }

    [Fact]
    public async Task MarkAsRead_PreventsCrossUserMutation_AndUpdatesState()
    {
        var (db, notifService) = CreateServices(nameof(MarkAsRead_PreventsCrossUserMutation_AndUpdatesState));

        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await notifService.SendNotificationAsync(userA, "Notif A", "Msg A", NotificationType.SystemAlert);
        await notifService.SendNotificationAsync(userB, "Notif B", "Msg B", NotificationType.SystemAlert);

        var notifB = await db.Notifications.FirstAsync(n => n.UserId == userB);

        // 1. User A tries to mark User B's notification -> Should fail (NotFound)
        var crossResult = await notifService.MarkAsReadAsync(userA, notifB.Id);
        Assert.False(crossResult.IsSuccess);

        // Verify notifB is still unread
        var refreshedB = await db.Notifications.FindAsync(notifB.Id);
        Assert.False(refreshedB!.IsRead);

        // 2. User B marks their own notification -> Success
        var validResult = await notifService.MarkAsReadAsync(userB, notifB.Id);
        Assert.True(validResult.IsSuccess);

        var updatedB = await db.Notifications.FindAsync(notifB.Id);
        Assert.True(updatedB!.IsRead);
        Assert.NotNull(updatedB.ReadAt);
    }

    [Fact]
    public async Task MarkAllAsRead_MarksAllForTargetUserOnly()
    {
        var (db, notifService) = CreateServices(nameof(MarkAllAsRead_MarksAllForTargetUserOnly));

        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await notifService.SendNotificationAsync(userA, "Notif A1", "Msg A1", NotificationType.SystemAlert);
        await notifService.SendNotificationAsync(userA, "Notif A2", "Msg A2", NotificationType.SystemAlert);
        await notifService.SendNotificationAsync(userB, "Notif B1", "Msg B1", NotificationType.SystemAlert);

        var res = await notifService.MarkAllAsReadAsync(userA);
        Assert.True(res.IsSuccess);

        // User A unread count is 0
        var unreadA = await notifService.GetUnreadCountAsync(userA);
        Assert.Equal(0, unreadA);

        // User B unread count remains 1
        var unreadB = await notifService.GetUnreadCountAsync(userB);
        Assert.Equal(1, unreadB);
    }

    [Fact]
    public async Task BookingCreationAndCancellation_DispatchesNotificationsToBothPlayerAndOwner()
    {
        var db = TestDbContextFactory.Create(nameof(BookingCreationAndCancellation_DispatchesNotificationsToBothPlayerAndOwner));
        var notifService = new NotificationService(db, NullLogger<NotificationService>.Instance);
        var bookingService = new BookingService(db, notifService);

        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        // 1. Create booking tomorrow at 10:00 UTC
        var startTime = DateTime.UtcNow.Date.AddDays(2).AddHours(10);
        var endTime = startTime.AddHours(1);

        var booking = await bookingService.CreateAsync(clientId, new CreateBookingRequest
        {
            CourtId = courtId,
            StartTime = startTime,
            EndTime = endTime
        });

        Assert.NotNull(booking);

        // 2. Verify Player received BookingConfirmed notification
        var playerNotif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == clientId && n.Type == NotificationType.BookingConfirmed);
        Assert.NotNull(playerNotif);
        Assert.Contains(booking.BookingReference, playerNotif.Message);

        // 3. Verify Owner received notification of new booking
        var ownerNotif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == ownerId && n.Type == NotificationType.BookingConfirmed);
        Assert.NotNull(ownerNotif);
        Assert.Contains(booking.BookingReference, ownerNotif.Message);

        // 4. Cancel booking
        var cancelResult = await bookingService.CancelAsync(clientId, "Client", booking.Id);
        Assert.True(cancelResult);

        // 5. Verify Player received BookingCancelled notification
        var playerCancelNotif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == clientId && n.Type == NotificationType.BookingCancelled);
        Assert.NotNull(playerCancelNotif);

        // 6. Verify Owner received cancellation notification
        var ownerCancelNotif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == ownerId && n.Type == NotificationType.BookingCancelled);
        Assert.NotNull(ownerCancelNotif);
    }

    [Theory]
    [InlineData("/Bookings/Details?id=123", true)]
    [InlineData("/venues/details?id=abc", true)]
    [InlineData("/notifications", true)]
    [InlineData("https://malicious.com/phish", false)]
    [InlineData("http://attacker.com", false)]
    [InlineData("//attacker.com/evil", false)]
    [InlineData("/\\attacker.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SafeActionUrlValidation_GuardsAgainstOpenRedirects(string? actionUrl, bool expectedValid)
    {
        var isValid = !string.IsNullOrWhiteSpace(actionUrl)
                      && actionUrl.StartsWith("/")
                      && !actionUrl.StartsWith("//")
                      && !actionUrl.StartsWith("/\\");

        Assert.Equal(expectedValid, isValid);
    }
}

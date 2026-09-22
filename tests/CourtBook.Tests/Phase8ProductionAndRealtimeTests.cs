using CourtBook.API.Hubs;
using CourtBook.API.Services;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.BackgroundJobs;
using CourtBook.Infrastructure.Persistence;
using CourtBook.Infrastructure.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace CourtBook.Tests;

public class Phase8ProductionAndRealtimeTests
{
    private static IServiceScopeFactory CreateMockScopeFactory(AppDbContext db, INotificationService? notifications = null)
    {
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(AppDbContext))).Returns(db);
        if (notifications != null)
        {
            serviceProviderMock.Setup(sp => sp.GetService(typeof(INotificationService))).Returns(notifications);
        }

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(sf => sf.CreateScope()).Returns(scopeMock.Object);

        return scopeFactoryMock.Object;
    }

    [Fact]
    public async Task PaymentHoldWorker_SweepsExpiredHolds_CancelsBookingAndReleasesSlot()
    {
        var dbName = $"Phase8_HoldWorker_{Guid.NewGuid():N}";
        using var db = TestDbContextFactory.Create(dbName);
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var now = DateTime.UtcNow;

        // 1. Expired hold (started 15 mins ago, expired 5 mins ago)
        var expiredBooking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-EXP-001",
            CourtId = courtId,
            UserId = clientId,
            StartTime = now.AddHours(2),
            EndTime = now.AddHours(3),
            Status = BookingStatus.Pending,
            PaymentStatus = PaymentStatus.Processing,
            TotalPrice = 200m
        };
        var expiredPayment = new Payment
        {
            Id = Guid.NewGuid(),
            BookingId = expiredBooking.Id,
            Amount = 200m,
            Method = PaymentMethod.CreditCard,
            Status = PaymentStatus.Processing,
            CreatedAt = now.AddMinutes(-15),
            ExpiresAt = now.AddMinutes(-5)
        };

        // 2. Active hold (started 2 mins ago, expires in 8 mins)
        var activeBooking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-ACT-002",
            CourtId = courtId,
            UserId = clientId,
            StartTime = now.AddHours(4),
            EndTime = now.AddHours(5),
            Status = BookingStatus.Pending,
            PaymentStatus = PaymentStatus.Processing,
            TotalPrice = 200m
        };
        var activePayment = new Payment
        {
            Id = Guid.NewGuid(),
            BookingId = activeBooking.Id,
            Amount = 200m,
            Method = PaymentMethod.CreditCard,
            Status = PaymentStatus.Processing,
            CreatedAt = now.AddMinutes(-2),
            ExpiresAt = now.AddMinutes(8)
        };

        // 3. Completed payment
        var completedBooking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-CMP-003",
            CourtId = courtId,
            UserId = clientId,
            StartTime = now.AddHours(6),
            EndTime = now.AddHours(7),
            Status = BookingStatus.Confirmed,
            PaymentStatus = PaymentStatus.Completed,
            TotalPrice = 200m
        };
        var completedPayment = new Payment
        {
            Id = Guid.NewGuid(),
            BookingId = completedBooking.Id,
            Amount = 200m,
            Method = PaymentMethod.CreditCard,
            Status = PaymentStatus.Completed,
            CreatedAt = now.AddMinutes(-30),
            ExpiresAt = now.AddMinutes(-20)
        };

        db.Bookings.AddRange(expiredBooking, activeBooking, completedBooking);
        db.Payments.AddRange(expiredPayment, activePayment, completedPayment);
        await db.SaveChangesAsync();

        var notificationMock = new Mock<INotificationService>();
        var scopeFactory = CreateMockScopeFactory(db, notificationMock.Object);
        var loggerMock = new Mock<ILogger<PaymentHoldWorker>>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PaymentHoldWorker:Enabled"] = "true",
                ["PaymentHoldWorker:IntervalSeconds"] = "60",
                ["PaymentHoldWorker:BatchSize"] = "10"
            })
            .Build();

        var worker = new PaymentHoldWorker(scopeFactory, loggerMock.Object, config);

        // Execute sweep
        var sweptCount = await worker.SweepExpiredHoldsAsync();

        Assert.Equal(1, sweptCount);

        // Verify expired payment & booking state
        var reloadedExpiredPayment = await db.Payments.FindAsync(expiredPayment.Id);
        var reloadedExpiredBooking = await db.Bookings.FindAsync(expiredBooking.Id);
        Assert.NotNull(reloadedExpiredPayment);
        Assert.NotNull(reloadedExpiredBooking);
        Assert.Equal(PaymentStatus.Failed, reloadedExpiredPayment.Status);
        Assert.Equal(PaymentStatus.Failed, reloadedExpiredBooking.PaymentStatus);
        Assert.Equal(BookingStatus.Cancelled, reloadedExpiredBooking.Status);
        Assert.NotNull(reloadedExpiredBooking.CancelledAt);
        Assert.Contains("hold expired", reloadedExpiredBooking.CancellationReason, StringComparison.OrdinalIgnoreCase);

        // Verify idempotency record written
        var idempotencyRecord = await db.IdempotencyLogs.FirstOrDefaultAsync(l => l.PaymentId == expiredPayment.Id);
        Assert.NotNull(idempotencyRecord);
        Assert.Equal("System", idempotencyRecord.Provider);
        Assert.Equal("HoldExpired", idempotencyRecord.Action);

        // Verify active payment was untouched
        var reloadedActivePayment = await db.Payments.FindAsync(activePayment.Id);
        var reloadedActiveBooking = await db.Bookings.FindAsync(activeBooking.Id);
        Assert.NotNull(reloadedActivePayment);
        Assert.NotNull(reloadedActiveBooking);
        Assert.Equal(PaymentStatus.Processing, reloadedActivePayment.Status);
        Assert.Equal(BookingStatus.Pending, reloadedActiveBooking.Status);

        // Verify completed payment was untouched
        var reloadedCompletedPayment = await db.Payments.FindAsync(completedPayment.Id);
        Assert.NotNull(reloadedCompletedPayment);
        Assert.Equal(PaymentStatus.Completed, reloadedCompletedPayment.Status);

        // Verify notification sent to user
        notificationMock.Verify(n => n.SendNotificationAsync(
            clientId,
            It.Is<string>(t => t.Contains("Expired")),
            It.IsAny<string>(),
            NotificationType.PaymentFailed,
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task PaymentHoldWorker_EmptyDatabase_ReturnsZeroWithoutErrors()
    {
        var dbName = $"Phase8_Empty_{Guid.NewGuid():N}";
        using var db = TestDbContextFactory.Create(dbName);
        var scopeFactory = CreateMockScopeFactory(db);
        var loggerMock = new Mock<ILogger<PaymentHoldWorker>>();
        var config = new ConfigurationBuilder().Build();

        var worker = new PaymentHoldWorker(scopeFactory, loggerMock.Object, config);
        var count = await worker.SweepExpiredHoldsAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task SignalRNotificationSender_PushesToIsolatedUserGroup()
    {
        var hubContextMock = new Mock<IHubContext<NotificationHub>>();
        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        var loggerMock = new Mock<ILogger<SignalRNotificationSender>>();

        var userId = Guid.NewGuid();
        var expectedGroup = $"user:{userId}";

        clientsMock.Setup(c => c.Group(expectedGroup)).Returns(clientProxyMock.Object);
        hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        var sender = new SignalRNotificationSender(hubContextMock.Object, loggerMock.Object);

        var payload = new { Title = "Match Ready", BookingId = Guid.NewGuid() };
        await sender.SendNotificationToUserAsync(userId, payload);

        clientProxyMock.Verify(cp => cp.SendCoreAsync(
            "ReceiveNotification",
            It.Is<object[]>(args => args.Length == 1 && args[0] == payload),
            default), Times.Once);
    }

    [Fact]
    public async Task NotificationService_DispatchesToRealTimeSender_WhenProvided()
    {
        var dbName = $"Phase8_NotifService_{Guid.NewGuid():N}";
        using var db = TestDbContextFactory.Create(dbName);

        var loggerMock = new Mock<ILogger<NotificationService>>();
        var realTimeSenderMock = new Mock<IRealTimeNotificationSender>();

        var service = new NotificationService(
            db,
            loggerMock.Object,
            emailSender: null,
            pushSender: null,
            realTimeSender: realTimeSenderMock.Object);

        var userId = Guid.NewGuid();
        await service.SendNotificationAsync(userId, "Goal Alert", "You scored!", NotificationType.SystemAlert, "/match/123");

        realTimeSenderMock.Verify(s => s.SendNotificationToUserAsync(
            userId,
            It.IsAny<object>(),
            default), Times.Once);

        // Verify stored in DB
        var stored = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == userId);
        Assert.NotNull(stored);
        Assert.Equal("Goal Alert", stored.Title);
        Assert.False(stored.IsRead);
    }

    [Fact]
    public async Task SeedData_DevelopmentMode_SeedsDefaultDevPassword()
    {
        var dbName = $"Phase8_SeedDev_{Guid.NewGuid():N}";
        using var db = TestDbContextFactory.Create(dbName);
        var loggerMock = new Mock<ILogger<AppDbContext>>();

        await SeedData.EnsureAdminAccountAsync(db, loggerMock.Object, isDevelopment: true);

        var admin = await db.Users.FirstOrDefaultAsync(u => u.Role == Role.Admin);
        Assert.NotNull(admin);
        Assert.Equal("admin@courtbook.eg", admin.Email);
        Assert.True(BCrypt.Net.BCrypt.Verify("Admin@123", admin.PasswordHash));
    }

    [Fact]
    public async Task SeedData_ProductionMode_DoesNotSeedStaticPassword()
    {
        var dbName = $"Phase8_SeedProd_{Guid.NewGuid():N}";
        using var db = TestDbContextFactory.Create(dbName);
        var loggerMock = new Mock<ILogger<AppDbContext>>();

        // Ensure env var is cleared for this test
        Environment.SetEnvironmentVariable("ADMIN_INITIAL_PASSWORD", null);
        Environment.SetEnvironmentVariable("Admin__InitialPassword", null);

        await SeedData.EnsureAdminAccountAsync(db, loggerMock.Object, isDevelopment: false);

        var admin = await db.Users.FirstOrDefaultAsync(u => u.Role == Role.Admin);
        Assert.NotNull(admin);
        Assert.Equal("admin@courtbook.eg", admin.Email);
        // In production mode without env var, static password Admin@123 must NOT match
        Assert.False(BCrypt.Net.BCrypt.Verify("Admin@123", admin.PasswordHash));
    }

    [Fact]
    public void PaymentConfiguration_ContainsStatusExpiresAtIndex()
    {
        var dbName = $"Phase8_Metadata_{Guid.NewGuid():N}";
        using var db = TestDbContextFactory.Create(dbName);

        var entityType = db.Model.FindEntityType(typeof(Payment));
        Assert.NotNull(entityType);

        var indexes = entityType.GetIndexes();
        var statusExpiresAtIndex = indexes.FirstOrDefault(idx =>
            idx.Properties.Count == 2 &&
            idx.Properties[0].Name == nameof(Payment.Status) &&
            idx.Properties[1].Name == nameof(Payment.ExpiresAt));

        Assert.NotNull(statusExpiresAtIndex);
    }
}

using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.BackgroundJobs;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CourtBook.Tests;

public class PaymentHoldWorkerTests
{
    private ServiceProvider CreateServiceProvider(string dbName, Dictionary<string, string?>? configDict = null)
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict ?? new Dictionary<string, string?>())
            .Build();

        services.AddSingleton<IConfiguration>(config);
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: dbName));

        services.AddLogging();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task PaymentHoldWorker_DisabledViaConfig_ExitsImmediatelyWithoutRunning()
    {
        var configDict = new Dictionary<string, string?>
        {
            ["PaymentHoldWorker:Enabled"] = "false"
        };
        var sp = CreateServiceProvider(nameof(PaymentHoldWorker_DisabledViaConfig_ExitsImmediatelyWithoutRunning), configDict);
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var config = sp.GetRequiredService<IConfiguration>();
        var logger = NullLogger<PaymentHoldWorker>.Instance;

        var worker = new PaymentHoldWorker(scopeFactory, logger, config);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await worker.StartAsync(cts.Token);
        await worker.StopAsync(CancellationToken.None);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(db.IdempotencyLogs);
    }

    [Fact]
    public async Task SweepExpiredHolds_ExpiredHoldsExist_CancelsHoldsReleasesSlotsLogsIdempotency()
    {
        var sp = CreateServiceProvider(nameof(SweepExpiredHolds_ExpiredHoldsExist_CancelsHoldsReleasesSlotsLogsIdempotency));
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var config = sp.GetRequiredService<IConfiguration>();
        var logger = NullLogger<PaymentHoldWorker>.Instance;

        Guid bookingId;
        Guid paymentId;

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

            var booking = new Booking
            {
                Id = Guid.NewGuid(),
                BookingReference = "HOLD-EXP-001",
                CourtId = courtId,
                UserId = clientId,
                StartTime = DateTime.UtcNow.AddHours(2),
                EndTime = DateTime.UtcNow.AddHours(3),
                TotalPrice = 500m,
                Status = BookingStatus.Pending,
                PaymentStatus = PaymentStatus.Processing
            };

            var payment = new Payment
            {
                Id = Guid.NewGuid(),
                BookingId = booking.Id,
                Amount = 500m,
                CommissionAmount = 25m,
                OwnerNetAmount = 475m,
                Status = PaymentStatus.Processing,
                Currency = "EGP",
                ExpiresAt = DateTime.UtcNow.AddMinutes(-5) // Expired 5 minutes ago
            };

            db.Bookings.Add(booking);
            db.Payments.Add(payment);
            await db.SaveChangesAsync();

            bookingId = booking.Id;
            paymentId = payment.Id;
        }

        var worker = new PaymentHoldWorker(scopeFactory, logger, config);
        var cancelledCount = await worker.SweepExpiredHoldsAsync(CancellationToken.None);

        Assert.Equal(1, cancelledCount);

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updatedBooking = await db.Bookings.FindAsync(bookingId);
            var updatedPayment = await db.Payments.FindAsync(paymentId);
            var idempotencyLog = await db.IdempotencyLogs.FirstOrDefaultAsync(l => l.PaymentId == paymentId);

            Assert.NotNull(updatedBooking);
            Assert.Equal(BookingStatus.Cancelled, updatedBooking.Status);
            Assert.Equal(PaymentStatus.Failed, updatedBooking.PaymentStatus);
            Assert.NotNull(updatedBooking.CancelledAt);

            Assert.NotNull(updatedPayment);
            Assert.Equal(PaymentStatus.Failed, updatedPayment.Status);

            Assert.NotNull(idempotencyLog);
            Assert.Equal("HoldExpired", idempotencyLog.Action);
            Assert.Equal($"hold-expire-{paymentId}", idempotencyLog.ProviderTransactionId);
        }
    }

    [Fact]
    public async Task SweepExpiredHolds_NoExpiredHolds_ReturnsZero()
    {
        var sp = CreateServiceProvider(nameof(SweepExpiredHolds_NoExpiredHolds_ReturnsZero));
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var config = sp.GetRequiredService<IConfiguration>();
        var logger = NullLogger<PaymentHoldWorker>.Instance;

        var worker = new PaymentHoldWorker(scopeFactory, logger, config);
        var cancelledCount = await worker.SweepExpiredHoldsAsync(CancellationToken.None);

        Assert.Equal(0, cancelledCount);
    }

    [Fact]
    public async Task SweepExpiredHolds_UnexpiredHold_DoesNotCancel()
    {
        var sp = CreateServiceProvider(nameof(SweepExpiredHolds_UnexpiredHold_DoesNotCancel));
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var config = sp.GetRequiredService<IConfiguration>();
        var logger = NullLogger<PaymentHoldWorker>.Instance;

        Guid bookingId;
        Guid paymentId;

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

            var booking = new Booking
            {
                Id = Guid.NewGuid(),
                BookingReference = "HOLD-ACTIVE-001",
                CourtId = courtId,
                UserId = clientId,
                StartTime = DateTime.UtcNow.AddHours(2),
                EndTime = DateTime.UtcNow.AddHours(3),
                TotalPrice = 500m,
                Status = BookingStatus.Pending,
                PaymentStatus = PaymentStatus.Processing
            };

            var payment = new Payment
            {
                Id = Guid.NewGuid(),
                BookingId = booking.Id,
                Amount = 500m,
                CommissionAmount = 25m,
                OwnerNetAmount = 475m,
                Status = PaymentStatus.Processing,
                Currency = "EGP",
                ExpiresAt = DateTime.UtcNow.AddMinutes(5) // Still valid for 5 minutes
            };

            db.Bookings.Add(booking);
            db.Payments.Add(payment);
            await db.SaveChangesAsync();

            bookingId = booking.Id;
            paymentId = payment.Id;
        }

        var worker = new PaymentHoldWorker(scopeFactory, logger, config);
        var cancelledCount = await worker.SweepExpiredHoldsAsync(CancellationToken.None);

        Assert.Equal(0, cancelledCount);

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updatedBooking = await db.Bookings.FindAsync(bookingId);
            var updatedPayment = await db.Payments.FindAsync(paymentId);

            Assert.NotNull(updatedBooking);
            Assert.Equal(BookingStatus.Pending, updatedBooking.Status);
            Assert.Equal(PaymentStatus.Processing, updatedBooking.PaymentStatus);

            Assert.NotNull(updatedPayment);
            Assert.Equal(PaymentStatus.Processing, updatedPayment.Status);
        }
    }

    [Fact]
    public async Task SweepExpiredHolds_InMemoryDatabase_ExecutesWithoutSqlLock()
    {
        var sp = CreateServiceProvider(nameof(SweepExpiredHolds_InMemoryDatabase_ExecutesWithoutSqlLock));
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var config = sp.GetRequiredService<IConfiguration>();
        var logger = NullLogger<PaymentHoldWorker>.Instance;

        var worker = new PaymentHoldWorker(scopeFactory, logger, config);
        // Should execute smoothly in non-relational in-memory mode without throwing or failing on sp_getapplock
        var result = await worker.SweepExpiredHoldsAsync(CancellationToken.None);
        Assert.Equal(0, result);
    }
}

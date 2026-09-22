using CourtBook.API.Health;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.BackgroundJobs;
using CourtBook.Infrastructure.Persistence;
using CourtBook.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CourtBook.Tests;

public class SettlementWorkerAndHealthCheckTests
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

        services.AddScoped<ISettlementService, SettlementService>();
        services.AddLogging();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SettlementWorker_DisabledViaConfig_ExitsImmediatelyWithoutRunning()
    {
        var configDict = new Dictionary<string, string?>
        {
            ["SettlementWorker:Enabled"] = "false"
        };
        var sp = CreateServiceProvider(nameof(SettlementWorker_DisabledViaConfig_ExitsImmediatelyWithoutRunning), configDict);
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var config = sp.GetRequiredService<IConfiguration>();
        var logger = NullLogger<SettlementWorker>.Instance;

        var worker = new SettlementWorker(scopeFactory, logger, config);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // When disabled, StartAsync completes quickly without looping
        await worker.StartAsync(cts.Token);
        await worker.StopAsync(CancellationToken.None);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(db.SettlementBatches);
    }

    [Fact]
    public async Task SettlementWorker_SweepSettlementsWithDistributedLock_ExecutesSettlementInNonRelationalMode()
    {
        var sp = CreateServiceProvider(nameof(SettlementWorker_SweepSettlementsWithDistributedLock_ExecutesSettlementInNonRelationalMode));
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var config = sp.GetRequiredService<IConfiguration>();
        var logger = NullLogger<SettlementWorker>.Instance;

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

            var booking = new Booking
            {
                Id = Guid.NewGuid(),
                BookingReference = "PS-SWEEP-001",
                CourtId = courtId,
                UserId = clientId,
                StartTime = DateTime.UtcNow.AddDays(-2),
                EndTime = DateTime.UtcNow.AddDays(-2).AddHours(1),
                TotalPrice = 1000m,
                Status = BookingStatus.Completed,
                PaymentStatus = PaymentStatus.Completed
            };
            var payment = new Payment
            {
                Id = Guid.NewGuid(),
                BookingId = booking.Id,
                Amount = 1000m,
                CommissionAmount = 50m,
                OwnerNetAmount = 950m,
                Status = PaymentStatus.Completed,
                Currency = "EGP"
            };
            db.Bookings.Add(booking);
            db.Payments.Add(payment);
            await db.SaveChangesAsync();
        }

        var worker = new SettlementWorker(scopeFactory, logger, config);
        await worker.SweepSettlementsWithDistributedLockAsync(24, CancellationToken.None);

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var batch = await db.SettlementBatches.FirstOrDefaultAsync();
            Assert.NotNull(batch);
            Assert.Equal(1, batch.ItemCount);
            Assert.Equal(950m, batch.TotalNet);
        }
    }

    [Fact]
    public async Task SettlementHealthCheck_RecentCompletedBatch_ReturnsHealthy()
    {
        var sp = CreateServiceProvider(nameof(SettlementHealthCheck_RecentCompletedBatch_ReturnsHealthy));
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var config = sp.GetRequiredService<IConfiguration>();
        var logger = NullLogger<SettlementHealthCheck>.Instance;

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.SettlementBatches.Add(new SettlementBatch
            {
                Id = Guid.NewGuid(),
                BatchReference = "SETTLE-HEALTH-01",
                PeriodStart = DateTime.UtcNow.AddDays(-2),
                PeriodEnd = DateTime.UtcNow.AddHours(-1),
                TotalGross = 1000m,
                TotalCommission = 50m,
                TotalNet = 950m,
                ItemCount = 1,
                Status = SettlementStatus.Completed,
                CreatedAt = DateTime.UtcNow.AddHours(-2) // 2 hours old, well within 36h
            });
            await db.SaveChangesAsync();
        }

        var healthCheck = new SettlementHealthCheck(scopeFactory, config, logger);
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("completed", result.Description);
    }

    [Fact]
    public async Task SettlementHealthCheck_BatchOlderThanThreshold_ReturnsDegraded()
    {
        var sp = CreateServiceProvider(nameof(SettlementHealthCheck_BatchOlderThanThreshold_ReturnsDegraded));
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var config = sp.GetRequiredService<IConfiguration>();
        var logger = NullLogger<SettlementHealthCheck>.Instance;

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.SettlementBatches.Add(new SettlementBatch
            {
                Id = Guid.NewGuid(),
                BatchReference = "SETTLE-HEALTH-02",
                PeriodStart = DateTime.UtcNow.AddDays(-5),
                PeriodEnd = DateTime.UtcNow.AddDays(-3),
                TotalGross = 1000m,
                TotalCommission = 50m,
                TotalNet = 950m,
                ItemCount = 1,
                Status = SettlementStatus.Completed,
                CreatedAt = DateTime.UtcNow.AddHours(-48) // 48 hours old, exceeds 36h threshold
            });
            await db.SaveChangesAsync();
        }

        var healthCheck = new SettlementHealthCheck(scopeFactory, config, logger);
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("exceeding the operational threshold", result.Description);
    }

    [Fact]
    public async Task SettlementHealthCheck_NoHistoricalBatches_NoOverdueBookings_ReturnsHealthy()
    {
        var sp = CreateServiceProvider(nameof(SettlementHealthCheck_NoHistoricalBatches_NoOverdueBookings_ReturnsHealthy));
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var config = sp.GetRequiredService<IConfiguration>();
        var logger = NullLogger<SettlementHealthCheck>.Instance;

        var healthCheck = new SettlementHealthCheck(scopeFactory, config, logger);
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("awaiting first eligible booking", result.Description);
    }

    [Fact]
    public async Task SettlementHealthCheck_NoHistoricalBatches_OverdueBookingsExist_ReturnsDegraded()
    {
        var sp = CreateServiceProvider(nameof(SettlementHealthCheck_NoHistoricalBatches_OverdueBookingsExist_ReturnsDegraded));
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var config = sp.GetRequiredService<IConfiguration>();
        var logger = NullLogger<SettlementHealthCheck>.Instance;

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

            var booking = new Booking
            {
                Id = Guid.NewGuid(),
                BookingReference = "PS-OVERDUE-001",
                CourtId = courtId,
                UserId = clientId,
                StartTime = DateTime.UtcNow.AddDays(-3),
                EndTime = DateTime.UtcNow.AddDays(-3).AddHours(1),
                TotalPrice = 1000m,
                Status = BookingStatus.Completed,
                PaymentStatus = PaymentStatus.Completed
            };
            var payment = new Payment
            {
                Id = Guid.NewGuid(),
                BookingId = booking.Id,
                Amount = 1000m,
                CommissionAmount = 50m,
                OwnerNetAmount = 950m,
                Status = PaymentStatus.Completed,
                Currency = "EGP"
            };
            db.Bookings.Add(booking);
            db.Payments.Add(payment);
            await db.SaveChangesAsync();
        }

        var healthCheck = new SettlementHealthCheck(scopeFactory, config, logger);
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("eligible cleared bookings awaiting first settlement", result.Description);
    }

    [Fact]
    public async Task SettlementService_UsesConfiguredCommissionRate()
    {
        var configDict = new Dictionary<string, string?>
        {
            ["PaymentGateway:CommissionRate"] = "0.08" // 8% commission
        };
        var sp = CreateServiceProvider(nameof(SettlementService_UsesConfiguredCommissionRate), configDict);
        var db = sp.GetRequiredService<AppDbContext>();
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-COMM-001",
            CourtId = courtId,
            UserId = clientId,
            StartTime = DateTime.UtcNow.AddDays(-2),
            EndTime = DateTime.UtcNow.AddDays(-2).AddHours(1),
            TotalPrice = 1000m,
            CancellationFee = 500m,
            Status = BookingStatus.Cancelled,
            PaymentStatus = PaymentStatus.PartiallyRefunded
        };
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            Amount = 1000m,
            CommissionAmount = 80m,
            OwnerNetAmount = 920m,
            Status = PaymentStatus.PartiallyRefunded,
            Currency = "EGP"
        };
        db.Bookings.Add(booking);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var settlementService = sp.GetRequiredService<ISettlementService>();
        var batch = await settlementService.ExecuteSettlementBatchAsync(24);

        // Retained cancellation fee = 500. Commission at 8% = 40. Realized net = 460.
        Assert.Equal(1, batch.ItemCount);
        Assert.Equal(40m, batch.TotalCommission);
        Assert.Equal(460m, batch.TotalNet);
    }
}

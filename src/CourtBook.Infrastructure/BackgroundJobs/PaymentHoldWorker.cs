using System.Data;
using System.Data.Common;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CourtBook.Infrastructure.BackgroundJobs;

/// <summary>
/// Background worker that periodically sweeps expired online payment holds.
/// If a player initiates online payment but abandons checkout (or 10 minutes elapse),
/// this worker marks the payment as Failed, marks the booking as Cancelled,
/// releases the reserved slot, logs an idempotency/audit record, and notifies the user.
/// Features SQL Server application-level distributed locking (sp_getapplock) for multi-instance safety.
/// </summary>
public class PaymentHoldWorker : BackgroundService
{
    public const string LockResourceName = "CourtBook:PaymentHoldWorker:SweepLock";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaymentHoldWorker> _logger;
    private readonly IConfiguration _configuration;

    public PaymentHoldWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<PaymentHoldWorker> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var isEnabled = !bool.TryParse(_configuration["PaymentHoldWorker:Enabled"], out var enabled) || enabled;
        if (!isEnabled)
        {
            _logger.LogInformation("PaymentHoldWorker is disabled via configuration.");
            return;
        }

        var intervalSeconds = int.TryParse(_configuration["PaymentHoldWorker:IntervalSeconds"], out var interval) && interval >= 5
            ? interval
            : 60;

        _logger.LogInformation(
            "PaymentHoldWorker started. Interval={IntervalSeconds}s, LockResource='{LockResource}'.",
            intervalSeconds, LockResourceName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepExpiredHoldsAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Unhandled error occurred while sweeping expired payment holds.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("PaymentHoldWorker stopped.");
    }

    public async Task<int> SweepExpiredHoldsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (!db.Database.IsRelational())
        {
            // Non-relational (In-Memory provider for unit/integration tests): execute directly without sp_getapplock
            return await ExecuteSweepCoreAsync(scope, db, cancellationToken);
        }

        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var lockAcquired = false;
        try
        {
            // 1. Attempt non-blocking distributed lock acquisition via SQL Server sp_getapplock
            await using (var getLockCmd = connection.CreateCommand())
            {
                getLockCmd.CommandText = @"
                    DECLARE @result INT;
                    EXEC @result = sp_getapplock
                        @Resource = @resourceName,
                        @LockMode = 'Exclusive',
                        @LockOwner = 'Session',
                        @LockTimeout = 0;
                    SELECT @result;";

                var param = getLockCmd.CreateParameter();
                param.ParameterName = "@resourceName";
                param.Value = LockResourceName;
                getLockCmd.Parameters.Add(param);

                var scalar = await getLockCmd.ExecuteScalarAsync(cancellationToken);
                var lockResult = scalar is not null ? Convert.ToInt32(scalar) : -999;

                // Return values: 0 = Granted synchronously, 1 = Granted after wait, < 0 = Timeout/held/error
                if (lockResult < 0)
                {
                    _logger.LogInformation(
                        "Payment hold sweep lock '{LockResource}' is currently held by another instance (code {Code}). Skipping this sweep cycle.",
                        LockResourceName, lockResult);
                    return 0;
                }

                lockAcquired = true;
                _logger.LogInformation("Acquired distributed lock '{LockResource}'. Starting payment hold sweep.", LockResourceName);
            }

            // 2. Execute hold sweep core logic
            return await ExecuteSweepCoreAsync(scope, db, cancellationToken);
        }
        finally
        {
            // 3. Release distributed lock
            if (lockAcquired && connection.State == ConnectionState.Open)
            {
                try
                {
                    await using var releaseCmd = connection.CreateCommand();
                    releaseCmd.CommandText = @"
                        EXEC sp_releaseapplock
                            @Resource = @resourceName,
                            @LockOwner = 'Session';";

                    var param = releaseCmd.CreateParameter();
                    param.ParameterName = "@resourceName";
                    param.Value = LockResourceName;
                    releaseCmd.Parameters.Add(param);

                    await releaseCmd.ExecuteNonQueryAsync(CancellationToken.None);
                    _logger.LogInformation("Released distributed lock '{LockResource}'.", LockResourceName);
                }
                catch (Exception releaseEx)
                {
                    _logger.LogWarning(releaseEx, "Error releasing distributed lock '{LockResource}'.", LockResourceName);
                }
            }
        }
    }

    private async Task<int> ExecuteSweepCoreAsync(
        IServiceScope scope,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        using (_logger.BeginScope(new Dictionary<string, object> { ["Worker"] = nameof(PaymentHoldWorker), ["LockResource"] = LockResourceName }))
        {
            var batchSize = int.TryParse(_configuration["PaymentHoldWorker:BatchSize"], out var batch) && batch >= 1
                ? batch
                : 50;

            var notifications = scope.ServiceProvider.GetService<INotificationService>();
            var now = DateTime.UtcNow;

            var expiredPayments = await db.Payments
                .Include(p => p.Booking)
                .Where(p => p.Status == PaymentStatus.Processing
                         && p.ExpiresAt != null
                         && p.ExpiresAt <= now
                         && p.Booking.Status != BookingStatus.Cancelled)
                .OrderBy(p => p.ExpiresAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (expiredPayments.Count == 0)
                return 0;

            _logger.LogInformation("Found {Count} expired payment holds to cancel.", expiredPayments.Count);

            foreach (var payment in expiredPayments)
            {
                payment.Status = PaymentStatus.Failed;
                payment.Booking.PaymentStatus = PaymentStatus.Failed;
                payment.Booking.Status = BookingStatus.Cancelled;
                payment.Booking.CancelledAt = now;
                payment.Booking.CancellationReason = "Online payment hold expired (10-minute window elapsed). Slot released.";

                db.IdempotencyLogs.Add(new IdempotencyLog
                {
                    Id = Guid.NewGuid(),
                    Provider = "System",
                    ProviderTransactionId = $"hold-expire-{payment.Id}",
                    ResponseStatusCode = 200,
                    Action = "HoldExpired",
                    PaymentId = payment.Id,
                    ProcessedAt = now
                });
            }

            await db.SaveChangesAsync(cancellationToken);

            if (notifications != null)
            {
                foreach (var payment in expiredPayments)
                {
                    try
                    {
                        await notifications.SendNotificationAsync(
                            payment.Booking.UserId,
                            "Booking Hold Expired ⏱️",
                            $"Your payment hold for booking {payment.Booking.BookingReference} expired and the slot has been released.",
                            NotificationType.PaymentFailed,
                            $"/Courts/Details?id={payment.Booking.CourtId}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to send expiration notification for payment {PaymentId}", payment.Id);
                    }
                }
            }

            _logger.LogInformation("Successfully cancelled {Count} expired payment holds and released court slots.", expiredPayments.Count);
            return expiredPayments.Count;
        }
    }
}

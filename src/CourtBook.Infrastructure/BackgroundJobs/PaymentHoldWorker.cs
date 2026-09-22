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
/// </summary>
public class PaymentHoldWorker : BackgroundService
{
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

        _logger.LogInformation("PaymentHoldWorker started with interval {IntervalSeconds}s.", intervalSeconds);

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
        var batchSize = int.TryParse(_configuration["PaymentHoldWorker:BatchSize"], out var batch) && batch >= 1
            ? batch
            : 50;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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

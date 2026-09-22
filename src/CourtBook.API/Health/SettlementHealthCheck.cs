using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace CourtBook.API.Health;

/// <summary>
/// Health check that evaluates persistent database settlement batch state.
/// Rule:
/// - Healthy if a successful settlement batch was completed within the configured threshold (default 36h).
/// - Degraded if the last completed batch is older than the threshold.
/// - If no batch has ever run: Healthy if no eligible overdue bookings exist; Degraded if eligible bookings are overdue.
/// </summary>
public class SettlementHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SettlementHealthCheck> _logger;

    public SettlementHealthCheck(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<SettlementHealthCheck> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var thresholdHours = int.TryParse(_configuration["HealthChecks:Settlement:ThresholdHours"], out var t) && t > 0
                ? t
                : 36;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var lastBatch = await db.SettlementBatches
                .AsNoTracking()
                .Where(b => b.Status == SettlementStatus.Completed)
                .OrderByDescending(b => b.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            var totalBatches = await db.SettlementBatches
                .AsNoTracking()
                .CountAsync(b => b.Status == SettlementStatus.Completed, cancellationToken);

            var bufferHours = int.TryParse(_configuration["SettlementWorker:BufferHours"], out var bHours) && bHours >= 0
                ? bHours
                : 24;
            var cutoff = DateTime.UtcNow.AddHours(-bufferHours);

            var overdueUnsettledBookings = await db.Bookings
                .AsNoTracking()
                .Where(b => b.EndTime <= cutoff
                         && b.Payment != null
                         && (b.Payment.Status == PaymentStatus.Completed || b.Payment.Status == PaymentStatus.PartiallyRefunded)
                         && b.PaymentStatus != PaymentStatus.Refunded
                         && !(b.Payment.Method == PaymentMethod.PayAtFacility && b.PaymentStatus == PaymentStatus.Pending)
                         && b.Payment.Status != PaymentStatus.Processing
                         && !db.SettlementItems.Any(s => s.BookingId == b.Id))
                .Select(b => b.EndTime)
                .ToListAsync(cancellationToken);

            var overdueCount = overdueUnsettledBookings.Count;
            var oldestOverdueAgeHours = overdueCount > 0
                ? Math.Round((DateTime.UtcNow - overdueUnsettledBookings.Min()).TotalHours, 1)
                : 0.0;

            var lastCompleted = lastBatch?.CreatedAt;
            var lastBatchAgeHours = lastCompleted.HasValue
                ? Math.Round((DateTime.UtcNow - lastCompleted.Value).TotalHours, 1)
                : (double?)null;

            var data = new Dictionary<string, object>
            {
                ["ThresholdHours"] = thresholdHours,
                ["TotalCompletedBatches"] = totalBatches,
                ["LastBatchReference"] = lastBatch?.BatchReference ?? "None",
                ["LastBatchAgeHours"] = lastBatchAgeHours.HasValue ? (object)lastBatchAgeHours.Value : "None",
                ["OverdueUnsettledBookingsCount"] = overdueCount,
                ["OldestUnsettledBookingAgeHours"] = oldestOverdueAgeHours
            };

            if (lastCompleted.HasValue && lastBatchAgeHours.HasValue)
            {
                if (lastBatchAgeHours.Value <= thresholdHours)
                {
                    return HealthCheckResult.Healthy(
                        $"Last settlement batch '{lastBatch!.BatchReference}' completed {lastBatchAgeHours.Value:0.0} hours ago at {lastCompleted:u}.", data);
                }

                return HealthCheckResult.Degraded(
                    $"Last settlement batch '{lastBatch!.BatchReference}' was completed {lastBatchAgeHours.Value:0.0} hours ago, exceeding the operational threshold of {thresholdHours} hours.",
                    null, data);
            }

            if (overdueCount > 0)
            {
                return HealthCheckResult.Degraded(
                    $"System has {overdueCount} eligible cleared bookings awaiting first settlement run.", null, data);
            }

            return HealthCheckResult.Healthy(
                "No historical settlement batches completed; system is awaiting first eligible booking clearing.", data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SettlementHealthCheck failed to evaluate persistent database state.");
            return HealthCheckResult.Unhealthy($"Settlement health check failed with error: {ex.Message}", ex);
        }
    }
}

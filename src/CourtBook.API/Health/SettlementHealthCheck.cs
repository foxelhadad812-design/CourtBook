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

            var lastCompleted = await db.SettlementBatches
                .AsNoTracking()
                .Where(b => b.Status == SettlementStatus.Completed)
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => (DateTime?)b.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            var totalBatches = await db.SettlementBatches
                .AsNoTracking()
                .CountAsync(b => b.Status == SettlementStatus.Completed, cancellationToken);

            var data = new Dictionary<string, object>
            {
                ["ThresholdHours"] = thresholdHours,
                ["TotalCompletedBatches"] = totalBatches,
                ["LastExecutionUtc"] = lastCompleted?.ToString("u") ?? "None"
            };

            if (lastCompleted.HasValue)
            {
                var age = DateTime.UtcNow - lastCompleted.Value;
                data["AgeHours"] = Math.Round(age.TotalHours, 1);

                if (age <= TimeSpan.FromHours(thresholdHours))
                {
                    return HealthCheckResult.Healthy(
                        $"Last settlement batch completed {age.TotalHours:0.0} hours ago at {lastCompleted:u}.", data);
                }

                return HealthCheckResult.Degraded(
                    $"Last settlement batch was completed {age.TotalHours:0.0} hours ago, exceeding the operational threshold of {thresholdHours} hours.",
                    null, data);
            }

            // Zero historical settlement batches ever completed
            var bufferHours = int.TryParse(_configuration["SettlementWorker:BufferHours"], out var bHours) && bHours >= 0
                ? bHours
                : 24;
            var cutoff = DateTime.UtcNow.AddHours(-bufferHours);

            var hasEligibleOverdueBookings = await db.Bookings
                .AsNoTracking()
                .AnyAsync(b => b.EndTime <= cutoff
                            && b.Payment != null
                            && (b.Payment.Status == PaymentStatus.Completed || b.Payment.Status == PaymentStatus.PartiallyRefunded)
                            && b.PaymentStatus != PaymentStatus.Refunded
                            && !(b.Payment.Method == PaymentMethod.PayAtFacility && b.PaymentStatus == PaymentStatus.Pending)
                            && b.Payment.Status != PaymentStatus.Processing
                            && !db.SettlementItems.Any(s => s.BookingId == b.Id),
                            cancellationToken);

            data["HasEligibleOverdueBookings"] = hasEligibleOverdueBookings;

            if (hasEligibleOverdueBookings)
            {
                return HealthCheckResult.Degraded(
                    "System has eligible cleared bookings awaiting first settlement run.", null, data);
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

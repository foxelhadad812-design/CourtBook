using System.Data;
using System.Data.Common;
using CourtBook.Application.Interfaces;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CourtBook.Infrastructure.BackgroundJobs;

/// <summary>
/// Background worker that periodically sweeps eligible bookings and executes settlement batches.
/// Features:
/// 1. Single execution scheduling model via IntervalHours and BufferHours.
/// 2. Multi-instance coordination via SQL Server application-level distributed locking (sp_getapplock).
///    Peer instances skip the sweep cycle if the lock is held.
/// 3. Resilient execution loop with structured error telemetry.
/// </summary>
public class SettlementWorker : BackgroundService
{
    public const string LockResourceName = "CourtBook:SettlementWorker:SweepLock";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SettlementWorker> _logger;
    private readonly IConfiguration _configuration;

    public SettlementWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<SettlementWorker> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var isEnabled = !bool.TryParse(_configuration["SettlementWorker:Enabled"], out var enabled) || enabled;
        if (!isEnabled)
        {
            _logger.LogInformation("SettlementWorker is disabled via configuration.");
            return;
        }

        var intervalHours = int.TryParse(_configuration["SettlementWorker:IntervalHours"], out var interval) && interval >= 1
            ? interval
            : 12;

        var bufferHours = int.TryParse(_configuration["SettlementWorker:BufferHours"], out var buffer) && buffer >= 0
            ? buffer
            : 24;

        _logger.LogInformation(
            "SettlementWorker started. Interval={IntervalHours}h, Buffer={BufferHours}h, LockResource='{LockResource}'.",
            intervalHours, bufferHours, LockResourceName);

        // Initial warmup delay allowing DI initialization, migrations, and seed data to complete
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepSettlementsWithDistributedLockAsync(bufferHours, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Unhandled error during settlement background sweep execution.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("SettlementWorker stopped.");
    }

    public async Task SweepSettlementsWithDistributedLockAsync(int bufferHours, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (!db.Database.IsRelational())
        {
            // Non-relational (In-Memory provider for unit/integration tests): execute directly without sp_getapplock
            var settlementService = scope.ServiceProvider.GetRequiredService<ISettlementService>();
            var result = await settlementService.ExecuteSettlementBatchAsync(bufferHours, null, ct);
            _logger.LogInformation("Settlement sweep executed (In-Memory). Batch={BatchRef}, Items={ItemCount}, Net=EGP {TotalNet}.",
                result.BatchReference, result.ItemCount, result.TotalNet);
            return;
        }

        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
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

                var scalar = await getLockCmd.ExecuteScalarAsync(ct);
                var lockResult = scalar is not null ? Convert.ToInt32(scalar) : -999;

                // Return values: 0 = Granted synchronously, 1 = Granted after wait, < 0 = Timeout/held/error
                if (lockResult < 0)
                {
                    _logger.LogInformation(
                        "Settlement sweep lock '{LockResource}' is currently held by another instance (code {Code}). Skipping this sweep cycle.",
                        LockResourceName, lockResult);
                    return;
                }

                lockAcquired = true;
                _logger.LogInformation("Acquired distributed lock '{LockResource}'. Starting settlement sweep.", LockResourceName);
            }

            // 2. Execute settlement sweep
            var service = scope.ServiceProvider.GetRequiredService<ISettlementService>();
            var batchDto = await service.ExecuteSettlementBatchAsync(bufferHours, null, ct);

            _logger.LogInformation(
                "Settlement sweep completed successfully. Batch={BatchRef}, Items={ItemCount}, Net=EGP {TotalNet:0.00}.",
                batchDto.BatchReference, batchDto.ItemCount, batchDto.TotalNet);
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
}

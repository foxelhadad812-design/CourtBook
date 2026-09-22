using System.Data;
using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CourtBook.Infrastructure.Services;

public class SettlementService : ISettlementService
{
    private readonly AppDbContext _db;
    private readonly ILogger<SettlementService> _logger;
    private readonly decimal _commissionRate;

    public SettlementService(
        AppDbContext db,
        ILogger<SettlementService> logger,
        IConfiguration? configuration = null)
    {
        _db = db;
        _logger = logger;

        if (configuration != null &&
            decimal.TryParse(configuration["PaymentGateway:CommissionRate"], out var rate) &&
            rate >= 0m)
        {
            _commissionRate = rate;
        }
        else
        {
            _commissionRate = 0.05m;
        }
    }

    public async Task<SettlementBatchDto> ExecuteSettlementBatchAsync(
        int bufferHours = 24, Guid? adminUserId = null, CancellationToken ct = default)
    {
        bufferHours = Math.Max(0, bufferHours);
        var cutoff = DateTime.UtcNow.AddHours(-bufferHours);

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
                : null;

            try
            {
                // 1. Query all eligible bookings strictly matching settlement conditions:
                // - Booking.EndTime + Buffer <= UtcNow
                // - Payment is Completed or PartiallyRefunded
                // - Excludes unpaid PayAtFacility and active holds
                // - Excludes fully refunded bookings (net realized = 0)
                // - No existing SettlementItem for BookingId (Hard Invariant)
                var eligibleBookings = await _db.Bookings
                    .Include(b => b.Payment)
                    .Include(b => b.Court).ThenInclude(c => c.Venue)
                    .Where(b => b.EndTime <= cutoff
                             && b.Payment != null
                             && (b.Payment.Status == PaymentStatus.Completed || b.Payment.Status == PaymentStatus.PartiallyRefunded)
                             && b.PaymentStatus != PaymentStatus.Refunded
                             && !(b.Payment.Method == PaymentMethod.PayAtFacility && b.PaymentStatus == PaymentStatus.Pending)
                             && b.Payment.Status != PaymentStatus.Processing
                             && !_db.SettlementItems.Any(s => s.BookingId == b.Id))
                    .OrderBy(b => b.Id)
                    .ToListAsync(ct);

                if (eligibleBookings.Count == 0)
                {
                    _logger.LogInformation("Settlement run executed. No eligible bookings found before cutoff {Cutoff}.", cutoff);
                    return new SettlementBatchDto
                    {
                        Id = Guid.NewGuid(),
                        BatchReference = $"SETTLE-EMPTY-{DateTime.UtcNow:yyyyMMddHHmmss}",
                        PeriodStart = cutoff,
                        PeriodEnd = cutoff,
                        Status = SettlementStatus.Completed.ToString(),
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = adminUserId ?? Guid.Empty,
                        ItemCount = 0
                    };
                }

                var batch = new SettlementBatch
                {
                    Id = Guid.NewGuid(),
                    BatchReference = $"SETTLE-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..22].ToUpperInvariant(),
                    PeriodStart = eligibleBookings.Min(b => b.StartTime),
                    PeriodEnd = cutoff,
                    Status = SettlementStatus.Completed,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = adminUserId ?? Guid.Empty
                };
                _db.SettlementBatches.Add(batch);

                decimal totalGross = 0m;
                decimal totalComm = 0m;
                decimal totalNet = 0m;

                // Group by venue owner to serialize mutations per owner cleanly
                var bookingsByOwner = eligibleBookings.GroupBy(b => b.Court?.Venue?.OwnerId ?? Guid.Empty);

                foreach (var group in bookingsByOwner)
                {
                    var ownerId = group.Key;
                    if (ownerId == Guid.Empty) continue;

                    // Lock and read OwnerBalance
                    var ownerBalance = await _db.OwnerBalances
                        .FirstOrDefaultAsync(ob => ob.OwnerId == ownerId, ct);

                    if (ownerBalance is null)
                    {
                        ownerBalance = new OwnerBalance
                        {
                            OwnerId = ownerId,
                            Currency = "EGP",
                            ConcurrencyStamp = Guid.NewGuid(),
                            UpdatedAt = DateTime.UtcNow
                        };
                        _db.OwnerBalances.Add(ownerBalance);
                    }

                    decimal ownerClearedNet = 0m;

                    foreach (var booking in group)
                    {
                        var payment = booking.Payment!;

                        // Calculate realized position at booking level
                        decimal realizedGross;
                        decimal realizedComm;
                        decimal realizedNet;

                        if (payment.Status == PaymentStatus.PartiallyRefunded)
                        {
                            realizedGross = booking.CancellationFee;
                            realizedComm = Math.Round(realizedGross * _commissionRate, 2);
                            realizedNet = realizedGross - realizedComm;
                        }
                        else
                        {
                            realizedGross = payment.Amount;
                            realizedComm = payment.CommissionAmount;
                            realizedNet = payment.OwnerNetAmount;
                        }

                        if (realizedNet <= 0m) continue;

                        var item = new SettlementItem
                        {
                            Id = Guid.NewGuid(),
                            SettlementBatchId = batch.Id,
                            BookingId = booking.Id,
                            PaymentId = payment.Id,
                            OwnerId = ownerId,
                            GrossAmount = realizedGross,
                            CommissionAmount = realizedComm,
                            NetAmount = realizedNet,
                            SettledAt = DateTime.UtcNow
                        };
                        _db.SettlementItems.Add(item);

                        totalGross += realizedGross;
                        totalComm += realizedComm;
                        totalNet += realizedNet;
                        ownerClearedNet += realizedNet;
                    }

                    if (ownerClearedNet > 0m)
                    {
                        // 1. Move cleared net from PendingBalance
                        ownerBalance.PendingBalance = Math.Max(0m, ownerBalance.PendingBalance - ownerClearedNet);

                        // 2. Outstanding Deficit Recovery (FIFO consumption)
                        if (ownerBalance.OutstandingDeficit > 0m)
                        {
                            var activeObligations = await _db.RecoveryObligations
                                .Where(o => o.OwnerId == ownerId && o.Status == RecoveryStatus.Active)
                                .OrderBy(o => o.CreatedAt)
                                .ThenBy(o => o.Id)
                                .ToListAsync(ct);

                            var remainingToApply = ownerClearedNet;

                            foreach (var obligation in activeObligations)
                            {
                                if (remainingToApply <= 0m) break;

                                var offset = Math.Min(obligation.RemainingDeficitAmount, remainingToApply);
                                obligation.RemainingDeficitAmount -= offset;
                                ownerBalance.OutstandingDeficit -= offset;
                                remainingToApply -= offset;

                                if (obligation.RemainingDeficitAmount <= 0m)
                                {
                                    obligation.Status = RecoveryStatus.Recovered;
                                    obligation.ResolvedAt = DateTime.UtcNow;
                                    obligation.ResolvedBySettlementBatchId = batch.Id;
                                }
                                obligation.ConcurrencyStamp = Guid.NewGuid();
                            }

                            // Excess cleared net credits to AvailableBalance
                            if (remainingToApply > 0m)
                            {
                                ownerBalance.AvailableBalance += remainingToApply;
                            }
                        }
                        else
                        {
                            ownerBalance.AvailableBalance += ownerClearedNet;
                        }

                        ownerBalance.ConcurrencyStamp = Guid.NewGuid();
                        ownerBalance.UpdatedAt = DateTime.UtcNow;
                    }
                }

                batch.TotalGross = totalGross;
                batch.TotalCommission = totalComm;
                batch.TotalNet = totalNet;
                batch.ItemCount = _db.ChangeTracker.Entries<SettlementItem>().Count(e => e.State == EntityState.Added);

                await _db.SaveChangesAsync(ct);
                if (tx is not null) await tx.CommitAsync(ct);

                _logger.LogInformation("SettlementBatch {BatchRef} completed successfully with {ItemCount} items, Net EGP {TotalNet}.",
                    batch.BatchReference, batch.ItemCount, batch.TotalNet);

                return MapToBatchDto(batch);
            }
            catch (Exception ex)
            {
                if (tx is not null) await tx.RollbackAsync(ct);
                _logger.LogError(ex, "SettlementBatch failed to execute.");
                throw;
            }
        });
    }

    public async Task<PagedResult<SettlementBatchDto>> GetSettlementBatchesAsync(int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.SettlementBatches.AsNoTracking();
        var total = await query.CountAsync(ct);
        var batches = await query
            .OrderByDescending(b => b.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return PagedResult<SettlementBatchDto>.From(batches.Select(MapToBatchDto).ToList(), total, page, pageSize);
    }

    public async Task<SettlementBatchDto?> GetSettlementBatchByIdAsync(Guid id, CancellationToken ct = default)
    {
        var batch = await _db.SettlementBatches
            .AsNoTracking()
            .Include(b => b.Items)
                .ThenInclude(i => i.Booking)
            .Include(b => b.Items)
                .ThenInclude(i => i.Owner)
            .FirstOrDefaultAsync(b => b.Id == id, ct);

        return batch is null ? null : MapToBatchDto(batch);
    }

    public async Task<SettlementSummaryDto> GetSettlementSummaryAsync(CancellationToken ct = default)
    {
        var totalAmount = await _db.SettlementBatches
            .Where(b => b.Status == SettlementStatus.Completed)
            .SumAsync(b => (decimal?)b.TotalNet, ct) ?? 0m;

        var totalBatches = await _db.SettlementBatches
            .CountAsync(b => b.Status == SettlementStatus.Completed, ct);

        var totalItems = await _db.SettlementItems.CountAsync(ct);

        var lastDate = await _db.SettlementBatches
            .Where(b => b.Status == SettlementStatus.Completed)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => (DateTime?)b.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return new SettlementSummaryDto
        {
            TotalSettledAmount = totalAmount,
            TotalSettledBatches = totalBatches,
            TotalSettledItems = totalItems,
            LastSettlementDate = lastDate
        };
    }

    private static SettlementBatchDto MapToBatchDto(SettlementBatch b) => new()
    {
        Id = b.Id,
        BatchReference = b.BatchReference,
        PeriodStart = b.PeriodStart,
        PeriodEnd = b.PeriodEnd,
        TotalGross = b.TotalGross,
        TotalCommission = b.TotalCommission,
        TotalNet = b.TotalNet,
        ItemCount = b.ItemCount,
        Status = b.Status.ToString(),
        CreatedAt = b.CreatedAt,
        CreatedBy = b.CreatedBy,
        Items = b.Items?.Select(i => new SettlementItemDto
        {
            Id = i.Id,
            SettlementBatchId = i.SettlementBatchId,
            BookingId = i.BookingId,
            BookingReference = i.Booking?.BookingReference ?? string.Empty,
            PaymentId = i.PaymentId,
            OwnerId = i.OwnerId,
            OwnerName = i.Owner?.Name ?? string.Empty,
            GrossAmount = i.GrossAmount,
            CommissionAmount = i.CommissionAmount,
            NetAmount = i.NetAmount,
            SettledAt = i.SettledAt
        }).ToList() ?? new()
    };
}

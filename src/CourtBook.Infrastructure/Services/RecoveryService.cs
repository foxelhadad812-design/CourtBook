using System.Data;
using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CourtBook.Infrastructure.Services;

public class RecoveryService : IRecoveryService
{
    private readonly AppDbContext _db;
    private readonly ILogger<RecoveryService> _logger;

    public RecoveryService(AppDbContext db, ILogger<RecoveryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PagedResult<RecoveryObligationDto>> GetRecoveryObligationsAsync(
        int page, int pageSize, string? status = null, Guid? ownerId = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.RecoveryObligations
            .AsNoTracking()
            .Include(o => o.Owner)
            .Include(o => o.Booking)
            .AsQueryable();

        if (ownerId.HasValue && ownerId.Value != Guid.Empty)
            query = query.Where(o => o.OwnerId == ownerId.Value);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<RecoveryStatus>(status, true, out var rStatus))
            query = query.Where(o => o.Status == rStatus);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return PagedResult<RecoveryObligationDto>.From(items.Select(MapToDto).ToList(), total, page, pageSize);
    }

    public async Task<RecoverySummaryDto> GetRecoverySummaryAsync(CancellationToken ct = default)
    {
        var activeDeficit = await _db.RecoveryObligations
            .Where(o => o.Status == RecoveryStatus.Active)
            .SumAsync(o => (decimal?)o.RemainingDeficitAmount, ct) ?? 0m;

        var activeCount = await _db.RecoveryObligations
            .CountAsync(o => o.Status == RecoveryStatus.Active, ct);

        var recoveredAmount = await _db.RecoveryObligations
            .Where(o => o.Status == RecoveryStatus.Recovered)
            .SumAsync(o => (decimal?)o.TotalDeficitAmount, ct) ?? 0m;

        var writtenOffAmount = await _db.RecoveryObligations
            .Where(o => o.Status == RecoveryStatus.WrittenOff)
            .SumAsync(o => (decimal?)o.TotalDeficitAmount, ct) ?? 0m;

        return new RecoverySummaryDto
        {
            TotalActiveDeficit = activeDeficit,
            ActiveObligationsCount = activeCount,
            TotalRecoveredAmount = recoveredAmount,
            TotalWrittenOffAmount = writtenOffAmount
        };
    }

    public async Task<RecoveryObligationDto> SettleObligationManuallyAsync(
        Guid obligationId, Guid adminId, ManualSettleRecoveryRequest request, CancellationToken ct = default)
    {
        if (request.Amount <= 0m)
            throw new ArgumentException("Settlement amount must be greater than zero.");

        if (string.IsNullOrWhiteSpace(request.ExternalReference))
            throw new ArgumentException("External bank/InstaPay payment reference is mandatory.");

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
                : null;

            try
            {
                var obligation = await _db.RecoveryObligations
                    .Include(o => o.Owner)
                    .Include(o => o.Booking)
                    .FirstOrDefaultAsync(o => o.Id == obligationId, ct);

                if (obligation is null) throw new KeyNotFoundException("Recovery obligation not found.");

                if (obligation.Status != RecoveryStatus.Active)
                    throw new InvalidOperationException($"Cannot settle obligation in status {obligation.Status}.");

                if (request.Amount > obligation.RemainingDeficitAmount)
                    throw new ArgumentException($"Settlement amount (EGP {request.Amount:0.00}) exceeds remaining deficit (EGP {obligation.RemainingDeficitAmount:0.00}).");

                var balance = await _db.OwnerBalances
                    .FirstOrDefaultAsync(b => b.OwnerId == obligation.OwnerId, ct);

                // Atomic co-mutation: update RemainingDeficitAmount and OwnerBalance.OutstandingDeficit together
                obligation.RemainingDeficitAmount -= request.Amount;
                if (balance is not null)
                {
                    balance.OutstandingDeficit = Math.Max(0m, balance.OutstandingDeficit - request.Amount);
                    balance.ConcurrencyStamp = Guid.NewGuid();
                    balance.UpdatedAt = DateTime.UtcNow;
                }

                if (obligation.RemainingDeficitAmount <= 0m)
                {
                    obligation.Status = RecoveryStatus.Recovered;
                    obligation.ResolvedAt = DateTime.UtcNow;
                }

                var note = $"[Manual Settle {DateTime.UtcNow:u}] Admin {adminId} recorded EGP {request.Amount:0.00}. Ref: {request.ExternalReference}. Notes: {request.Notes}";
                obligation.AdminNotes = string.IsNullOrWhiteSpace(obligation.AdminNotes)
                    ? note
                    : $"{obligation.AdminNotes}\n{note}";

                obligation.ConcurrencyStamp = Guid.NewGuid();

                await _db.SaveChangesAsync(ct);
                if (tx is not null) await tx.CommitAsync(ct);

                _logger.LogInformation("Recovery obligation {Ref} manually settled for EGP {Amount} by admin {AdminId}.",
                    obligation.ObligationReference, request.Amount, adminId);

                return MapToDto(obligation);
            }
            catch
            {
                if (tx is not null) await tx.RollbackAsync(ct);
                throw;
            }
        });
    }

    public async Task<RecoveryObligationDto> WriteOffObligationAsync(
        Guid obligationId, Guid adminId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Write-off justification reason is mandatory.");

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
                : null;

            try
            {
                var obligation = await _db.RecoveryObligations
                    .Include(o => o.Owner)
                    .Include(o => o.Booking)
                    .FirstOrDefaultAsync(o => o.Id == obligationId, ct);

                if (obligation is null) throw new KeyNotFoundException("Recovery obligation not found.");

                if (obligation.Status != RecoveryStatus.Active)
                    throw new InvalidOperationException($"Cannot write off obligation in status {obligation.Status}.");

                var unrecoveredAmount = obligation.RemainingDeficitAmount;

                var balance = await _db.OwnerBalances
                    .FirstOrDefaultAsync(b => b.OwnerId == obligation.OwnerId, ct);

                // Atomic co-mutation: clear remaining deficit and adjust OwnerBalance
                obligation.RemainingDeficitAmount = 0m;
                obligation.Status = RecoveryStatus.WrittenOff;
                obligation.ResolvedAt = DateTime.UtcNow;

                if (balance is not null)
                {
                    balance.OutstandingDeficit = Math.Max(0m, balance.OutstandingDeficit - unrecoveredAmount);
                    balance.ConcurrencyStamp = Guid.NewGuid();
                    balance.UpdatedAt = DateTime.UtcNow;
                }

                var note = $"[Write-Off {DateTime.UtcNow:u}] Admin {adminId} wrote off uncollectible deficit of EGP {unrecoveredAmount:0.00}. Reason: {reason}";
                obligation.AdminNotes = string.IsNullOrWhiteSpace(obligation.AdminNotes)
                    ? note
                    : $"{obligation.AdminNotes}\n{note}";

                obligation.ConcurrencyStamp = Guid.NewGuid();

                await _db.SaveChangesAsync(ct);
                if (tx is not null) await tx.CommitAsync(ct);

                _logger.LogWarning("Recovery obligation {Ref} written off for EGP {Amount} by admin {AdminId}. Reason: {Reason}",
                    obligation.ObligationReference, unrecoveredAmount, adminId, reason);

                return MapToDto(obligation);
            }
            catch
            {
                if (tx is not null) await tx.RollbackAsync(ct);
                throw;
            }
        });
    }

    private static RecoveryObligationDto MapToDto(RecoveryObligation o) => new()
    {
        Id = o.Id,
        ObligationReference = o.ObligationReference,
        OwnerId = o.OwnerId,
        OwnerName = o.Owner?.Name ?? string.Empty,
        BookingId = o.BookingId,
        BookingReference = o.Booking?.BookingReference ?? string.Empty,
        PaymentId = o.PaymentId,
        TotalDeficitAmount = o.TotalDeficitAmount,
        RemainingDeficitAmount = o.RemainingDeficitAmount,
        Currency = o.Currency,
        Status = o.Status.ToString(),
        Reason = o.Reason,
        CreatedAt = o.CreatedAt,
        ResolvedAt = o.ResolvedAt,
        ResolvedBySettlementBatchId = o.ResolvedBySettlementBatchId,
        AdminNotes = o.AdminNotes
    };
}

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

public class PayoutService : IPayoutService
{
    private readonly AppDbContext _db;
    private readonly ILogger<PayoutService> _logger;
    private readonly INotificationService? _notifications;

    public PayoutService(AppDbContext db, ILogger<PayoutService> logger, INotificationService? notifications = null)
    {
        _db = db;
        _logger = logger;
        _notifications = notifications;
    }

    // ── Owner Operations ────────────────────────────────────────────────────

    public async Task<OwnerBalanceDto> GetOwnerBalanceAsync(Guid ownerId, CancellationToken ct = default)
    {
        var balance = await _db.OwnerBalances
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.OwnerId == ownerId, ct);

        if (balance is null)
        {
            return new OwnerBalanceDto
            {
                OwnerId = ownerId,
                PendingBalance = 0m,
                AvailableBalance = 0m,
                InFlightBalance = 0m,
                TotalPaidOut = 0m,
                TotalRefunded = 0m,
                OutstandingDeficit = 0m,
                Currency = "EGP",
                UpdatedAt = DateTime.UtcNow
            };
        }

        return new OwnerBalanceDto
        {
            OwnerId = balance.OwnerId,
            PendingBalance = balance.PendingBalance,
            AvailableBalance = balance.AvailableBalance,
            InFlightBalance = balance.InFlightBalance,
            TotalPaidOut = balance.TotalPaidOut,
            TotalRefunded = balance.TotalRefunded,
            OutstandingDeficit = balance.OutstandingDeficit,
            Currency = balance.Currency,
            UpdatedAt = balance.UpdatedAt
        };
    }

    public async Task<List<PayoutMethodDto>> GetPayoutMethodsAsync(Guid ownerId, CancellationToken ct = default)
    {
        var methods = await _db.OwnerPayoutMethods
            .AsNoTracking()
            .Where(m => m.OwnerId == ownerId && m.IsActive)
            .OrderByDescending(m => m.IsDefault)
            .ThenByDescending(m => m.CreatedAt)
            .ToListAsync(ct);

        return methods.Select(MapToMethodDto).ToList();
    }

    public async Task<PayoutMethodDto> CreatePayoutMethodAsync(
        Guid ownerId, CreatePayoutMethodRequest request, CancellationToken ct = default)
    {
        if (!Enum.TryParse<PayoutMethodType>(request.Type, true, out var methodType))
            throw new ArgumentException($"Invalid payout method type: {request.Type}");

        // If requested as default, unmark existing default methods
        if (request.IsDefault)
        {
            var existingDefaults = await _db.OwnerPayoutMethods
                .Where(m => m.OwnerId == ownerId && m.IsDefault)
                .ToListAsync(ct);

            foreach (var m in existingDefaults)
            {
                m.IsDefault = false;
                m.UpdatedAt = DateTime.UtcNow;
            }
        }
        else
        {
            // If this is the owner's first payout method, make it default automatically
            var hasAny = await _db.OwnerPayoutMethods.AnyAsync(m => m.OwnerId == ownerId && m.IsActive, ct);
            if (!hasAny) request.IsDefault = true;
        }

        var method = new OwnerPayoutMethod
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Type = methodType,
            AccountHolderName = request.AccountHolderName.Trim(),
            BankName = request.BankName?.Trim(),
            Iban = request.Iban?.Replace(" ", "").Trim().ToUpperInvariant(),
            AccountNumber = request.AccountNumber?.Trim(),
            InstaPayAddress = request.InstaPayAddress?.Trim().ToLowerInvariant(),
            MobileWalletNumber = request.MobileWalletNumber?.Replace(" ", "").Replace("-", "").Trim(),
            IsDefault = request.IsDefault,
            IsActive = true,
            IsVerified = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.NewGuid()
        };

        _db.OwnerPayoutMethods.Add(method);
        await _db.SaveChangesAsync(ct);

        return MapToMethodDto(method);
    }

    public async Task<bool> DeletePayoutMethodAsync(Guid ownerId, Guid methodId, CancellationToken ct = default)
    {
        var method = await _db.OwnerPayoutMethods
            .FirstOrDefaultAsync(m => m.Id == methodId && m.OwnerId == ownerId, ct);

        if (method is null) return false;

        // Soft deactivation to preserve foreign key integrity on historical payout requests
        method.IsActive = false;
        method.IsDefault = false;
        method.UpdatedAt = DateTime.UtcNow;
        method.ConcurrencyStamp = Guid.NewGuid();

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetDefaultPayoutMethodAsync(Guid ownerId, Guid methodId, CancellationToken ct = default)
    {
        var targetMethod = await _db.OwnerPayoutMethods
            .FirstOrDefaultAsync(m => m.Id == methodId && m.OwnerId == ownerId && m.IsActive, ct);

        if (targetMethod is null) return false;

        var allMethods = await _db.OwnerPayoutMethods
            .Where(m => m.OwnerId == ownerId && m.IsActive)
            .ToListAsync(ct);

        foreach (var m in allMethods)
        {
            m.IsDefault = (m.Id == methodId);
            m.UpdatedAt = DateTime.UtcNow;
            m.ConcurrencyStamp = Guid.NewGuid();
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<PayoutRequestDto> RequestPayoutAsync(
        Guid ownerId, CreatePayoutRequest request, CancellationToken ct = default)
    {
        if (request.Amount < 100m)
            throw new ArgumentException("Minimum payout withdrawal is EGP 100.00.");

        // 1. Idempotency Check
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existingLog = await _db.IdempotencyLogs
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Provider == "OwnerPayout" && l.ProviderTransactionId == $"{ownerId}:{request.IdempotencyKey}", ct);

            if (existingLog is not null)
            {
                var existingPayout = await _db.PayoutRequests
                    .Include(r => r.Owner)
                    .Include(r => r.PayoutMethod)
                    .FirstOrDefaultAsync(r => r.Id == existingLog.PaymentId, ct);

                if (existingPayout is not null)
                    return MapToPayoutDto(existingPayout);
            }
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
                : null;

            try
            {
                // Verify destination method belongs to owner and is active
                var payoutMethod = await _db.OwnerPayoutMethods
                    .FirstOrDefaultAsync(m => m.Id == request.PayoutMethodId && m.OwnerId == ownerId && m.IsActive, ct);

                if (payoutMethod is null)
                    throw new KeyNotFoundException("Active payout method not found.");

                // Lock & verify OwnerBalance
                var balance = await _db.OwnerBalances
                    .FirstOrDefaultAsync(b => b.OwnerId == ownerId, ct);

                if (balance is null || balance.AvailableBalance < request.Amount)
                {
                    var available = balance?.AvailableBalance ?? 0m;
                    throw new InvalidOperationException($"Insufficient available balance for withdrawal. Requested: EGP {request.Amount:0.00}, Available: EGP {available:0.00}.");
                }

                // Invariant: Payout blocked if there are active recovery obligations
                if (balance.OutstandingDeficit > 0m)
                {
                    throw new InvalidOperationException(
                        $"Payout blocked: Owner has an outstanding recovery deficit of EGP {balance.OutstandingDeficit:0.00}. Future settlements will automatically recover this balance.");
                }

                // Atomic reservation of funds
                balance.AvailableBalance -= request.Amount;
                balance.InFlightBalance += request.Amount;
                balance.ConcurrencyStamp = Guid.NewGuid();
                balance.UpdatedAt = DateTime.UtcNow;

                var fee = 0.00m; // MVP: zero fee
                var netAmount = request.Amount - fee;

                var payout = new PayoutRequest
                {
                    Id = Guid.NewGuid(),
                    PayoutReference = $"PO-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
                    OwnerId = ownerId,
                    PayoutMethodId = request.PayoutMethodId,
                    Amount = request.Amount,
                    Fee = fee,
                    NetAmount = netAmount,
                    Currency = "EGP",
                    Status = PayoutStatus.Submitted,
                    SubmittedAt = DateTime.UtcNow,
                    ConcurrencyStamp = Guid.NewGuid()
                };

                _db.PayoutRequests.Add(payout);

                if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    _db.IdempotencyLogs.Add(new IdempotencyLog
                    {
                        Id = Guid.NewGuid(),
                        Provider = "OwnerPayout",
                        ProviderTransactionId = $"{ownerId}:{request.IdempotencyKey}",
                        ResponseStatusCode = 200,
                        Action = "PayoutRequested",
                        PaymentId = payout.Id,
                        ProcessedAt = DateTime.UtcNow
                    });
                }

                await _db.SaveChangesAsync(ct);
                if (tx is not null) await tx.CommitAsync(ct);

                // Load navigation for DTO
                payout.Owner = (await _db.Users.FindAsync(new object[] { ownerId }, ct))!;
                payout.PayoutMethod = payoutMethod;

                return MapToPayoutDto(payout);
            }
            catch (Exception ex)
            {
                if (tx is not null) await tx.RollbackAsync(ct);
                _logger.LogError(ex, "Failed to submit payout request for owner {OwnerId}", ownerId);
                throw;
            }
        });
    }

    public async Task<PagedResult<PayoutRequestDto>> GetOwnerPayoutsAsync(
        Guid ownerId, int page, int pageSize, string? status = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.PayoutRequests
            .AsNoTracking()
            .Include(r => r.Owner)
            .Include(r => r.PayoutMethod)
            .Where(r => r.OwnerId == ownerId);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PayoutStatus>(status, true, out var pStatus))
            query = query.Where(r => r.Status == pStatus);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.SubmittedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return PagedResult<PayoutRequestDto>.From(items.Select(MapToPayoutDto).ToList(), total, page, pageSize);
    }

    public async Task<PayoutRequestDto?> GetOwnerPayoutByIdAsync(Guid ownerId, Guid payoutId, CancellationToken ct = default)
    {
        var payout = await _db.PayoutRequests
            .AsNoTracking()
            .Include(r => r.Owner)
            .Include(r => r.PayoutMethod)
            .FirstOrDefaultAsync(r => r.Id == payoutId && r.OwnerId == ownerId, ct);

        return payout is null ? null : MapToPayoutDto(payout);
    }

    public async Task<bool> CancelPayoutRequestAsync(Guid ownerId, Guid payoutId, CancellationToken ct = default)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
                : null;

            try
            {
                var payout = await _db.PayoutRequests
                    .FirstOrDefaultAsync(r => r.Id == payoutId && r.OwnerId == ownerId, ct);

                if (payout is null) return false;

                if (payout.Status != PayoutStatus.Submitted)
                    throw new InvalidOperationException($"Cannot cancel payout request in state {payout.Status}. Only Submitted requests can be cancelled.");

                var balance = await _db.OwnerBalances
                    .FirstOrDefaultAsync(b => b.OwnerId == ownerId, ct);

                if (balance is not null)
                {
                    // Restore reserved funds
                    balance.InFlightBalance = Math.Max(0m, balance.InFlightBalance - payout.Amount);
                    balance.AvailableBalance += payout.Amount;
                    balance.ConcurrencyStamp = Guid.NewGuid();
                    balance.UpdatedAt = DateTime.UtcNow;
                }

                payout.Status = PayoutStatus.Cancelled;
                payout.RejectionReason = "Cancelled by owner prior to review.";
                payout.ConcurrencyStamp = Guid.NewGuid();

                await _db.SaveChangesAsync(ct);
                if (tx is not null) await tx.CommitAsync(ct);

                return true;
            }
            catch
            {
                if (tx is not null) await tx.RollbackAsync(ct);
                throw;
            }
        });
    }

    // ── Admin Operations ────────────────────────────────────────────────────

    public async Task<PagedResult<PayoutRequestDto>> GetAdminPayoutsAsync(
        int page, int pageSize, string? status = null, Guid? ownerId = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.PayoutRequests
            .AsNoTracking()
            .Include(r => r.Owner)
            .Include(r => r.PayoutMethod)
            .AsQueryable();

        if (ownerId.HasValue && ownerId.Value != Guid.Empty)
            query = query.Where(r => r.OwnerId == ownerId.Value);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PayoutStatus>(status, true, out var pStatus))
            query = query.Where(r => r.Status == pStatus);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.SubmittedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return PagedResult<PayoutRequestDto>.From(items.Select(MapToPayoutDto).ToList(), total, page, pageSize);
    }

    public async Task<PayoutRequestDto?> GetAdminPayoutByIdAsync(Guid payoutId, CancellationToken ct = default)
    {
        var payout = await _db.PayoutRequests
            .AsNoTracking()
            .Include(r => r.Owner)
            .Include(r => r.PayoutMethod)
            .FirstOrDefaultAsync(r => r.Id == payoutId, ct);

        return payout is null ? null : MapToPayoutDto(payout);
    }

    public async Task<PayoutRequestDto> ApprovePayoutAsync(Guid payoutId, Guid adminId, CancellationToken ct = default)
    {
        var payout = await _db.PayoutRequests
            .Include(r => r.Owner)
            .Include(r => r.PayoutMethod)
            .FirstOrDefaultAsync(r => r.Id == payoutId, ct);

        if (payout is null) throw new KeyNotFoundException("Payout request not found.");

        if (payout.OwnerId == adminId)
            throw new InvalidOperationException("Administrative self-approval of payouts is strictly prohibited.");

        if (payout.Status != PayoutStatus.Submitted)
            throw new InvalidOperationException($"Cannot approve payout in state {payout.Status}. Must be Submitted.");

        payout.Status = PayoutStatus.Approved;
        payout.ApprovedByAdminId = adminId;
        payout.ApprovedAt = DateTime.UtcNow;
        payout.ConcurrencyStamp = Guid.NewGuid();

        await _db.SaveChangesAsync(ct);
        return MapToPayoutDto(payout);
    }

    public async Task<PayoutRequestDto> RejectPayoutAsync(Guid payoutId, Guid adminId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Rejection reason is required.");

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
                : null;

            try
            {
                var payout = await _db.PayoutRequests
                    .Include(r => r.Owner)
                    .Include(r => r.PayoutMethod)
                    .FirstOrDefaultAsync(r => r.Id == payoutId, ct);

                if (payout is null) throw new KeyNotFoundException("Payout request not found.");

                if (payout.OwnerId == adminId)
                    throw new InvalidOperationException("Administrative self-action on payouts is strictly prohibited.");

                if (payout.Status != PayoutStatus.Submitted && payout.Status != PayoutStatus.Approved)
                    throw new InvalidOperationException($"Cannot reject payout in state {payout.Status}.");

                var balance = await _db.OwnerBalances
                    .FirstOrDefaultAsync(b => b.OwnerId == payout.OwnerId, ct);

                if (balance is not null)
                {
                    // Restore reserved funds
                    balance.InFlightBalance = Math.Max(0m, balance.InFlightBalance - payout.Amount);
                    balance.AvailableBalance += payout.Amount;
                    balance.ConcurrencyStamp = Guid.NewGuid();
                    balance.UpdatedAt = DateTime.UtcNow;
                }

                payout.Status = PayoutStatus.Rejected;
                payout.RejectionReason = reason.Trim();
                payout.ConcurrencyStamp = Guid.NewGuid();

                await _db.SaveChangesAsync(ct);
                if (tx is not null) await tx.CommitAsync(ct);

                return MapToPayoutDto(payout);
            }
            catch
            {
                if (tx is not null) await tx.RollbackAsync(ct);
                throw;
            }
        });
    }

    public async Task<PayoutRequestDto> MarkPayoutPaidAsync(
        Guid payoutId, Guid adminId, MarkPayoutPaidRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ExternalTransactionReference))
            throw new ArgumentException("External transaction reference is mandatory.");

        var refClean = request.ExternalTransactionReference.Trim();

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
                : null;

            try
            {
                // Check unique constraint on external reference
                var refUsed = await _db.PayoutRequests
                    .AnyAsync(r => r.ExternalTransactionReference == refClean && r.Id != payoutId, ct);

                if (refUsed)
                    throw new InvalidOperationException($"External transaction reference '{refClean}' has already been recorded for another payout.");

                var payout = await _db.PayoutRequests
                    .Include(r => r.Owner)
                    .Include(r => r.PayoutMethod)
                    .FirstOrDefaultAsync(r => r.Id == payoutId, ct);

                if (payout is null) throw new KeyNotFoundException("Payout request not found.");

                if (payout.OwnerId == adminId)
                    throw new InvalidOperationException("Administrative self-disbursement is strictly prohibited.");

                if (payout.Status != PayoutStatus.Approved && payout.Status != PayoutStatus.Submitted)
                    throw new InvalidOperationException($"Cannot mark payout Paid from status {payout.Status}.");

                var balance = await _db.OwnerBalances
                    .FirstOrDefaultAsync(b => b.OwnerId == payout.OwnerId, ct);

                if (balance is not null)
                {
                    // Move from InFlightBalance to TotalPaidOut
                    balance.InFlightBalance = Math.Max(0m, balance.InFlightBalance - payout.Amount);
                    balance.TotalPaidOut += payout.Amount;
                    balance.ConcurrencyStamp = Guid.NewGuid();
                    balance.UpdatedAt = DateTime.UtcNow;
                }

                payout.Status = PayoutStatus.Paid;
                payout.ExternalTransactionReference = refClean;
                payout.DisbursementNote = request.DisbursementNote?.Trim();
                payout.DisbursedByAdminId = adminId;
                payout.PaidAt = DateTime.UtcNow;
                payout.ConcurrencyStamp = Guid.NewGuid();

                // Append immutable OwnerPayout record in TransactionLedger
                _db.TransactionLedger.Add(new TransactionLedger
                {
                    Id = Guid.NewGuid(),
                    PaymentId = null,
                    BookingId = null,
                    PayoutRequestId = payout.Id,
                    UserId = payout.OwnerId,
                    OwnerId = payout.OwnerId,
                    EntryType = LedgerEntryType.OwnerPayout,
                    GrossAmount = -payout.Amount,
                    CommissionAmount = 0m,
                    NetAmount = -payout.Amount,
                    CommissionRateSnapshot = 0m,
                    Currency = payout.Currency,
                    Description = $"Payout #{payout.PayoutReference} disbursed via {payout.PayoutMethod?.Type}. Ref: {refClean}",
                    ProviderReference = refClean,
                    CreatedAt = DateTime.UtcNow
                });

                await _db.SaveChangesAsync(ct);
                if (tx is not null) await tx.CommitAsync(ct);

                if (_notifications is not null)
                {
                    try
                    {
                        await _notifications.SendNotificationAsync(
                            payout.OwnerId,
                            "Payout Disbursed 💸",
                            $"Your payout request of EGP {payout.NetAmount:0.00} has been processed via {payout.PayoutMethod?.Type}. Ref: {refClean}",
                            NotificationType.SystemAlert,
                            "/Owner/Payouts");
                    }
                    catch { /* best effort */ }
                }

                return MapToPayoutDto(payout);
            }
            catch
            {
                if (tx is not null) await tx.RollbackAsync(ct);
                throw;
            }
        });
    }

    public async Task<AdminPayoutSummaryDto> GetAdminPayoutSummaryAsync(CancellationToken ct = default)
    {
        var pendingReview = await _db.PayoutRequests
            .Where(r => r.Status == PayoutStatus.Submitted)
            .GroupBy(_ => 1)
            .Select(g => new { Total = g.Sum(r => r.Amount), Count = g.Count() })
            .FirstOrDefaultAsync(ct);

        var approved = await _db.PayoutRequests
            .Where(r => r.Status == PayoutStatus.Approved)
            .GroupBy(_ => 1)
            .Select(g => new { Total = g.Sum(r => r.Amount), Count = g.Count() })
            .FirstOrDefaultAsync(ct);

        var paid = await _db.PayoutRequests
            .Where(r => r.Status == PayoutStatus.Paid)
            .GroupBy(_ => 1)
            .Select(g => new { Total = g.Sum(r => r.Amount), Count = g.Count() })
            .FirstOrDefaultAsync(ct);

        return new AdminPayoutSummaryDto
        {
            TotalPendingReviewAmount = pendingReview?.Total ?? 0m,
            PendingReviewCount = pendingReview?.Count ?? 0,
            TotalApprovedAmount = approved?.Total ?? 0m,
            ApprovedCount = approved?.Count ?? 0,
            TotalPaidAmount = paid?.Total ?? 0m,
            PaidCount = paid?.Count ?? 0
        };
    }

    // ── Helper Mappings ─────────────────────────────────────────────────────

    private static PayoutMethodDto MapToMethodDto(OwnerPayoutMethod m)
    {
        string? maskedIban = null;
        if (!string.IsNullOrWhiteSpace(m.Iban))
        {
            var iban = m.Iban.Trim();
            maskedIban = iban.Length > 6
                ? $"{iban[..4]}********************{iban[^4..]}"
                : "****";
        }

        string? maskedAcc = null;
        if (!string.IsNullOrWhiteSpace(m.AccountNumber))
        {
            var acc = m.AccountNumber.Trim();
            maskedAcc = acc.Length > 4
                ? $"******{acc[^4..]}"
                : "****";
        }

        string? maskedIpa = null;
        if (!string.IsNullOrWhiteSpace(m.InstaPayAddress))
        {
            var ipa = m.InstaPayAddress.Trim();
            var atIdx = ipa.IndexOf('@');
            if (atIdx > 2)
                maskedIpa = $"{ipa[0]}***{ipa[atIdx - 1]}{ipa[atIdx..]}";
            else
                maskedIpa = ipa;
        }

        string? maskedWallet = null;
        if (!string.IsNullOrWhiteSpace(m.MobileWalletNumber))
        {
            var num = m.MobileWalletNumber.Trim();
            maskedWallet = num.Length >= 10
                ? $"{num[..3]}*****{num[^3..]}"
                : "****";
        }

        return new PayoutMethodDto
        {
            Id = m.Id,
            OwnerId = m.OwnerId,
            Type = m.Type.ToString(),
            AccountHolderName = m.AccountHolderName,
            BankName = m.BankName,
            MaskedIban = maskedIban,
            MaskedAccountNumber = maskedAcc,
            MaskedInstaPayAddress = maskedIpa,
            MaskedMobileWalletNumber = maskedWallet,
            IsDefault = m.IsDefault,
            IsActive = m.IsActive,
            IsVerified = m.IsVerified,
            CreatedAt = m.CreatedAt
        };
    }

    private static PayoutRequestDto MapToPayoutDto(PayoutRequest r)
    {
        var destSummary = r.PayoutMethod != null
            ? $"{r.PayoutMethod.Type} - {r.PayoutMethod.AccountHolderName}"
            : string.Empty;

        return new PayoutRequestDto
        {
            Id = r.Id,
            PayoutReference = r.PayoutReference,
            OwnerId = r.OwnerId,
            OwnerName = r.Owner?.Name ?? string.Empty,
            PayoutMethodId = r.PayoutMethodId,
            PayoutMethodType = r.PayoutMethod?.Type.ToString() ?? string.Empty,
            DestinationSummary = destSummary,
            Amount = r.Amount,
            Fee = r.Fee,
            NetAmount = r.NetAmount,
            Currency = r.Currency,
            Status = r.Status.ToString(),
            RejectionReason = r.RejectionReason,
            ExternalTransactionReference = r.ExternalTransactionReference,
            DisbursementNote = r.DisbursementNote,
            SubmittedAt = r.SubmittedAt,
            ApprovedAt = r.ApprovedAt,
            PaidAt = r.PaidAt
        };
    }
}

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

/// <summary>
/// Application-level payment orchestration service.
/// Manages the full payment lifecycle: initiation, webhook processing,
/// refunds, ledger entries, and financial reporting.
///
/// KEY SECURITY INVARIANTS:
/// - Server always calculates amounts from authoritative booking/court data.
/// - Payment is only marked Completed after trusted server-side gateway verification.
/// - Idempotency log prevents duplicate webhook processing.
/// - Ledger entries are never deleted.
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly AppDbContext _db;
    private readonly IPaymentGatewayService _gateway;
    private readonly INotificationService? _notifications;
    private readonly ILogger<PaymentService> _logger;
    private readonly decimal _commissionRate;

    private const string OnlineHoldMinutesKey = "PaymentGateway:OnlineHoldMinutes";
    private const int DefaultOnlineHoldMinutes = 10;
    private const decimal DefaultCommissionRate = 0.05m; // 5%

    public PaymentService(
        AppDbContext db,
        IPaymentGatewayService gateway,
        ILogger<PaymentService> logger,
        IConfiguration configuration,
        INotificationService? notifications = null)
    {
        _db            = db;
        _gateway       = gateway;
        _logger        = logger;
        _notifications = notifications;

        _commissionRate = decimal.TryParse(
            configuration["PaymentGateway:CommissionRate"], out var rate)
            ? rate
            : DefaultCommissionRate;
    }

    // ── Initiation ──────────────────────────────────────────────────────────

    public async Task<InitiatePaymentResponse> InitiateOnlinePaymentAsync(
        Guid userId, Guid bookingId, string paymentMethodStr, string returnBaseUrl)
    {
        var booking = await _db.Bookings
            .Include(b => b.Court)
                .ThenInclude(c => c.Venue)
            .Include(b => b.User)
            .Include(b => b.Payment)
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.UserId == userId);

        if (booking is null)
            throw new KeyNotFoundException("Booking not found.");

        if (booking.Status == BookingStatus.Cancelled)
            throw new InvalidOperationException("Cannot pay for a cancelled booking.");

        // Prevent paying twice
        if (booking.Payment?.Status == PaymentStatus.Completed)
            throw new InvalidOperationException("This booking has already been paid.");

        // Server-calculated amount — never trust client
        var amount = booking.TotalPrice;

        if (!Enum.TryParse<PaymentMethod>(paymentMethodStr, true, out var method))
            method = PaymentMethod.CreditCard;


        // Create or update the payment record
        var payment = booking.Payment;
        if (payment is null)
        {
            payment = new Payment
            {
                Id        = Guid.NewGuid(),
                BookingId = bookingId,
                Amount    = amount,
                Currency  = "EGP",
                Method    = method,
                Status    = PaymentStatus.Processing,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(DefaultOnlineHoldMinutes)
            };
            _db.Payments.Add(payment);
        }
        else
        {
            payment.Method    = method;
            payment.Status    = PaymentStatus.Processing;
            payment.Amount    = amount; // re-sync server amount
            payment.ExpiresAt = DateTime.UtcNow.AddMinutes(DefaultOnlineHoldMinutes);
        }

        // Update booking payment status
        booking.PaymentStatus = PaymentStatus.Processing;
        await _db.SaveChangesAsync();

        // Initiate with provider
        var initiationReq = new PaymentInitiationRequest(
            BookingId:     bookingId,
            Amount:        amount,
            Currency:      "EGP",
            CustomerEmail: booking.User?.Email ?? "player@playspot.eg",
            CustomerName:  booking.User?.Name  ?? "Player",
            CustomerPhone: booking.User?.Phone ?? "N/A",
            ReturnUrl:     $"{returnBaseUrl}/Payments/Success?orderId=__ORDER_ID__",
            CallbackUrl:   $"{returnBaseUrl}/api/payments/webhook");

        var result = await _gateway.InitiatePaymentAsync(initiationReq);

        if (!result.Success)
        {
            // Revert to Pending on initiation failure
            payment.Status      = PaymentStatus.Pending;
            booking.PaymentStatus = PaymentStatus.Pending;
            await _db.SaveChangesAsync();

            return new InitiatePaymentResponse
            {
                Success      = false,
                BookingId    = bookingId,
                ErrorMessage = result.ErrorMessage
            };
        }

        // Persist provider order ID and URL
        payment.ProviderOrderId = result.ProviderOrderId;
        payment.PaymentUrl      = result.PaymentUrl;
        await _db.SaveChangesAsync();

        return new InitiatePaymentResponse
        {
            Success       = true,
            PaymentId     = payment.Id,
            BookingId     = bookingId,
            PaymentUrl    = result.PaymentUrl,
            ProviderOrderId = result.ProviderOrderId,
            Amount        = amount,
            Currency      = "EGP",
            PaymentMethod = method.ToString(),
            Status        = payment.Status.ToString(),
            ExpiresAt     = payment.ExpiresAt
        };
    }

    public async Task<InitiatePaymentResponse> SelectPayAtFacilityAsync(Guid userId, Guid bookingId)
    {
        var booking = await _db.Bookings
            .Include(b => b.Payment)
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.UserId == userId);

        if (booking is null)
            throw new KeyNotFoundException("Booking not found.");

        if (booking.Status == BookingStatus.Cancelled)
            throw new InvalidOperationException("Cannot set payment method for a cancelled booking.");

        if (booking.Payment?.Status == PaymentStatus.Completed)
            throw new InvalidOperationException("This booking has already been paid.");

        var amount = booking.TotalPrice;

        var payment = booking.Payment;
        if (payment is null)
        {
            payment = new Payment
            {
                Id        = Guid.NewGuid(),
                BookingId = bookingId,
                Amount    = amount,
                Currency  = "EGP",
                Method    = PaymentMethod.PayAtFacility,
                Status    = PaymentStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };
            _db.Payments.Add(payment);
        }
        else
        {
            payment.Method = PaymentMethod.PayAtFacility;
            payment.Status = PaymentStatus.Pending;
        }

        booking.PaymentStatus = PaymentStatus.Pending;
        await _db.SaveChangesAsync();

        return new InitiatePaymentResponse
        {
            Success       = true,
            PaymentId     = payment.Id,
            BookingId     = bookingId,
            Amount        = amount,
            Currency      = "EGP",
            PaymentMethod = PaymentMethod.PayAtFacility.ToString(),
            Status        = PaymentStatus.Pending.ToString()
        };
    }

    // ── Webhook Engine ──────────────────────────────────────────────────────

    public async Task ProcessWebhookPaymentCompletedAsync(
        string providerOrderId, string transactionRef, decimal amountPaid,
        string idempotencyKey, string provider)
    {
        // 1. Idempotency check — prevent duplicate processing
        var alreadyProcessed = await _db.IdempotencyLogs
            .AnyAsync(l => l.Provider == provider && l.ProviderTransactionId == idempotencyKey);

        if (alreadyProcessed)
        {
            _logger.LogInformation("Duplicate webhook ignored: Provider={Provider}, TxId={TxId}",
                provider, idempotencyKey);
            return;
        }

        // 2. Find payment by provider order ID
        var payment = await _db.Payments
            .Include(p => p.Booking)
                .ThenInclude(b => b.Court)
                    .ThenInclude(c => c.Venue)
            .FirstOrDefaultAsync(p => p.ProviderOrderId == providerOrderId);

        if (payment is null)
        {
            _logger.LogWarning("Webhook received for unknown order {OrderId}", providerOrderId);
            // Still record idempotency to avoid replay
            await RecordIdempotencyAsync(provider, idempotencyKey, null, "UnknownOrder");
            return;
        }

        // 3. Guard: only transition from Processing (or Pending) → Completed
        if (payment.Status == PaymentStatus.Completed)
        {
            _logger.LogInformation("Payment {PaymentId} already completed — skipping.", payment.Id);
            await RecordIdempotencyAsync(provider, idempotencyKey, payment.Id, "AlreadyCompleted");
            return;
        }

        // Invalid transitions: cannot complete an already Refunded, PartiallyRefunded, Cancelled, or Failed payment
        if (payment.Status == PaymentStatus.Refunded ||
            payment.Status == PaymentStatus.PartiallyRefunded ||
            payment.Status == PaymentStatus.Cancelled ||
            payment.Status == PaymentStatus.Failed ||
            payment.Booking.Status == BookingStatus.Cancelled)
        {
            _logger.LogWarning("Cannot complete payment {PaymentId} with terminal status {Status} or cancelled booking.",
                payment.Id, payment.Status);
            await RecordIdempotencyAsync(provider, idempotencyKey, payment.Id, $"TerminalState_{payment.Status}");
            return;
        }

        // 3b. 10-Minute Hold Expiration check
        if (payment.ExpiresAt.HasValue && payment.ExpiresAt.Value < DateTime.UtcNow && payment.Status == PaymentStatus.Processing)
        {
            _logger.LogWarning("Payment hold expired for Payment {PaymentId} at {ExpiresAt}. Rejecting completion.",
                payment.Id, payment.ExpiresAt.Value);
            payment.Status = PaymentStatus.Failed;
            payment.Booking.PaymentStatus = PaymentStatus.Failed;
            await _db.SaveChangesAsync();
            await RecordIdempotencyAsync(provider, idempotencyKey, payment.Id, "HoldExpired");
            return;
        }

        // 4. Server-side amount integrity check
        if (amountPaid < payment.Amount)
        {
            _logger.LogWarning("Amount mismatch: expected {Expected}, received {Actual} for Payment {PaymentId}",
                payment.Amount, amountPaid, payment.Id);
            payment.Status = PaymentStatus.Failed;
            payment.Booking.PaymentStatus = PaymentStatus.Failed;
            await _db.SaveChangesAsync();
            await RecordIdempotencyAsync(provider, idempotencyKey, payment.Id, "AmountMismatch");
            return;
        }

        // 5. Mark payment completed (inside a transaction for data consistency)
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted)
                : null;
            try
            {
                payment.Status                = PaymentStatus.Completed;
                payment.TransactionReference  = transactionRef;
                payment.PaidAt                = DateTime.UtcNow;
                payment.Booking.PaymentStatus = PaymentStatus.Completed;

                // Calculate commission and owner net
                var commission = Math.Round(amountPaid * _commissionRate, 2);
                var ownerNet   = Math.Round(amountPaid - commission, 2);
                payment.CommissionAmount = commission;
                payment.OwnerNetAmount   = ownerNet;

                var ownerId = payment.Booking.Court?.Venue?.OwnerId ?? Guid.Empty;

                // 6. Write ledger entries
                _db.TransactionLedger.Add(new TransactionLedger
                {
                    Id                     = Guid.NewGuid(),
                    PaymentId              = payment.Id,
                    BookingId              = payment.BookingId,
                    UserId                 = payment.Booking.UserId,
                    OwnerId                = ownerId,
                    EntryType              = LedgerEntryType.Payment,
                    GrossAmount            = amountPaid,
                    CommissionAmount       = commission,
                    NetAmount              = ownerNet,
                    CommissionRateSnapshot = _commissionRate,
                    Currency               = payment.Currency,
                    Description            = $"Payment for booking {payment.Booking.BookingReference}",
                    ProviderReference      = transactionRef,
                    CreatedAt              = DateTime.UtcNow
                });

                // 7. Record idempotency
                _db.IdempotencyLogs.Add(new IdempotencyLog
                {
                    Id                    = Guid.NewGuid(),
                    Provider              = provider,
                    ProviderTransactionId = idempotencyKey,
                    ResponseStatusCode    = 200,
                    Action                = "PaymentCompleted",
                    PaymentId             = payment.Id,
                    ProcessedAt           = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();
                if (tx is not null) await tx.CommitAsync();
            }
            catch
            {
                if (tx is not null) await tx.RollbackAsync();
                throw;
            }
        });

        // 8. Payment receipt notification (outside transaction)
        if (_notifications is not null)
        {
            try
            {
                await _notifications.SendNotificationAsync(
                    payment.Booking.UserId,
                    "Payment Received ✅",
                    $"Payment of EGP {amountPaid:0.00} confirmed for booking {payment.Booking.BookingReference}.",
                    NotificationType.PaymentReceipt,
                    $"/Bookings/Details?id={payment.BookingId}");

                var ownerId = payment.Booking.Court?.Venue?.OwnerId ?? Guid.Empty;
                if (ownerId != Guid.Empty)
                {
                    await _notifications.SendNotificationAsync(
                        ownerId,
                        "Payment Received 💰",
                        $"Payment received for booking {payment.Booking.BookingReference}. Net: EGP {payment.OwnerNetAmount:0.00}.",
                        NotificationType.PaymentReceived,
                        "/owner/bookings");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send payment notification for booking {BookingId}", payment.BookingId);
            }
        }
    }

    // ── Return URL Verification ─────────────────────────────────────────────

    public async Task<PaymentVerificationResponse> VerifyReturnAsync(Guid userId, string userRole, string providerOrderId)
    {
        var payment = await _db.Payments
            .Include(p => p.Booking)
            .FirstOrDefaultAsync(p => p.ProviderOrderId == providerOrderId);

        if (payment is null)
            return new PaymentVerificationResponse
            {
                IsSuccessful = false,
                ErrorMessage = "Payment record not found."
            };

        // Strict player authorization check
        if (userId != Guid.Empty && payment.Booking.UserId != userId && !string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("You do not have permission to view or verify this payment.");
        }

        // Guard against verifying already terminated/failed payments
        if (payment.Status == PaymentStatus.Failed ||
            payment.Status == PaymentStatus.Cancelled ||
            payment.Status == PaymentStatus.Refunded ||
            payment.Status == PaymentStatus.PartiallyRefunded)
        {
            return new PaymentVerificationResponse
            {
                IsSuccessful         = false,
                BookingId            = payment.BookingId,
                Status               = payment.Status.ToString(),
                TransactionReference = payment.TransactionReference,
                Amount               = payment.Amount,
                ErrorMessage         = $"Payment cannot be verified because it is already in terminal state: {payment.Status}."
            };
        }

        // Check if hold has expired
        if (payment.ExpiresAt.HasValue && payment.ExpiresAt.Value < DateTime.UtcNow && payment.Status == PaymentStatus.Processing)
        {
            payment.Status = PaymentStatus.Failed;
            payment.Booking.PaymentStatus = PaymentStatus.Failed;
            await _db.SaveChangesAsync();

            return new PaymentVerificationResponse
            {
                IsSuccessful         = false,
                BookingId            = payment.BookingId,
                Status               = PaymentStatus.Failed.ToString(),
                TransactionReference = payment.TransactionReference,
                Amount               = payment.Amount,
                ErrorMessage         = "Payment hold expired (10 minutes elapsed). Please initiate a new booking."
            };
        }

        // Server-side verification — never trust browser callback alone
        var verification = await _gateway.VerifyPaymentAsync(providerOrderId);

        if (verification.IsSuccessful && payment.Status != PaymentStatus.Completed)
        {
            // Webhook may have already completed it — if not, update here
            await ProcessWebhookPaymentCompletedAsync(
                providerOrderId,
                verification.TransactionReference ?? providerOrderId,
                verification.AmountPaid ?? payment.Amount,
                $"return-{providerOrderId}",
                _gateway.ProviderName);
        }
        else if (!verification.IsSuccessful && payment.Status == PaymentStatus.Processing)
        {
            payment.Status                = PaymentStatus.Failed;
            payment.Booking.PaymentStatus = PaymentStatus.Failed;
            await _db.SaveChangesAsync();

            if (_notifications is not null)
            {
                try
                {
                    await _notifications.SendNotificationAsync(
                        payment.Booking.UserId,
                        "Payment Failed ❌",
                        $"Payment for booking {payment.Booking.BookingReference} could not be completed. Please try again.",
                        NotificationType.PaymentFailed,
                        $"/Payments/Failed?bookingId={payment.BookingId}");
                }
                catch { /* best effort */ }
            }
        }

        // Re-read final status
        await _db.Entry(payment).ReloadAsync();

        return new PaymentVerificationResponse
        {
            IsSuccessful         = payment.Status == PaymentStatus.Completed,
            BookingId            = payment.BookingId,
            Status               = payment.Status.ToString(),
            TransactionReference = payment.TransactionReference,
            Amount               = payment.Amount
        };
    }

    // ── Refund Engine ───────────────────────────────────────────────────────

    public async Task<RefundResponse> ProcessRefundAsync(Guid bookingId, string reason)
    {
        var payment = await _db.Payments
            .Include(p => p.Booking)
                .ThenInclude(b => b.Court)
                    .ThenInclude(c => c.Venue)
            .FirstOrDefaultAsync(p => p.BookingId == bookingId);

        if (payment is null)
            return new RefundResponse { Success = false, ErrorMessage = "Payment not found." };

        var booking = payment.Booking;
        if (booking is null)
            return new RefundResponse { Success = false, ErrorMessage = "Associated booking not found." };

        if (payment.Status == PaymentStatus.Refunded || payment.Status == PaymentStatus.PartiallyRefunded)
            return new RefundResponse { Success = false, ErrorMessage = "Payment has already been refunded." };

        if (payment.Status != PaymentStatus.Completed)
            return new RefundResponse { Success = false, ErrorMessage = "Only completed payments can be refunded." };

        // Respect cancellation policy fee: refund = paid amount - cancellation fee
        var cancellationFee = booking.CancellationFee;
        var eligibleRefundAmount = Math.Max(0m, payment.Amount - cancellationFee);
        var isPartial = eligibleRefundAmount > 0 && eligibleRefundAmount < payment.Amount;
        var newStatus = isPartial ? PaymentStatus.PartiallyRefunded : PaymentStatus.Refunded;

        // Zero refund case (e.g. 100% late fee)
        if (eligibleRefundAmount == 0m)
        {
            payment.Status = PaymentStatus.Completed; // remains completed, full penalty retained

            _db.TransactionLedger.Add(new TransactionLedger
            {
                Id                     = Guid.NewGuid(),
                PaymentId              = payment.Id,
                BookingId              = bookingId,
                UserId                 = booking.UserId,
                OwnerId                = booking.Court?.Venue?.OwnerId ?? Guid.Empty,
                EntryType              = LedgerEntryType.CancellationFee,
                GrossAmount            = cancellationFee,
                CommissionAmount       = Math.Round(cancellationFee * _commissionRate, 2),
                NetAmount              = cancellationFee - Math.Round(cancellationFee * _commissionRate, 2),
                CommissionRateSnapshot = _commissionRate,
                Currency               = payment.Currency,
                Description            = $"Late cancellation fee retained (no refund): {reason}",
                CreatedAt              = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            return new RefundResponse { Success = true, RefundAmount = 0m, RefundTransactionId = "RETAINED_FEE" };
        }

        var commissionReversed = Math.Round(eligibleRefundAmount * _commissionRate, 2);
        var ownerNetReversed   = Math.Round(eligibleRefundAmount - commissionReversed, 2);

        // For PayAtFacility — no gateway refund needed
        if (payment.Method == PaymentMethod.PayAtFacility)
        {
            payment.Status        = newStatus;
            booking.PaymentStatus = newStatus;

            _db.TransactionLedger.Add(new TransactionLedger
            {
                Id                     = Guid.NewGuid(),
                PaymentId              = payment.Id,
                BookingId              = bookingId,
                UserId                 = booking.UserId,
                OwnerId                = booking.Court?.Venue?.OwnerId ?? Guid.Empty,
                EntryType              = LedgerEntryType.Refund,
                GrossAmount            = -eligibleRefundAmount,
                CommissionAmount       = -commissionReversed,
                NetAmount              = -ownerNetReversed,
                CommissionRateSnapshot = _commissionRate,
                Currency               = payment.Currency,
                Description            = $"Pay-at-facility {(isPartial ? "partial " : "")}refund: {reason}",
                CreatedAt              = DateTime.UtcNow
            });

            if (cancellationFee > 0m)
            {
                _db.TransactionLedger.Add(new TransactionLedger
                {
                    Id                     = Guid.NewGuid(),
                    PaymentId              = payment.Id,
                    BookingId              = bookingId,
                    UserId                 = booking.UserId,
                    OwnerId                = booking.Court?.Venue?.OwnerId ?? Guid.Empty,
                    EntryType              = LedgerEntryType.CancellationFee,
                    GrossAmount            = cancellationFee,
                    CommissionAmount       = Math.Round(cancellationFee * _commissionRate, 2),
                    NetAmount              = cancellationFee - Math.Round(cancellationFee * _commissionRate, 2),
                    CommissionRateSnapshot = _commissionRate,
                    Currency               = payment.Currency,
                    Description            = $"Retained cancellation fee on refund: {reason}",
                    CreatedAt              = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync();
            return new RefundResponse { Success = true, RefundAmount = eligibleRefundAmount };
        }

        // Online payment — call gateway
        var refundRequest = new RefundRequest(
            ProviderTransactionId: payment.TransactionReference ?? string.Empty,
            Amount: eligibleRefundAmount,
            Reason: reason);

        var refundResult = await _gateway.RefundAsync(refundRequest);

        if (!refundResult.Success)
            return new RefundResponse { Success = false, ErrorMessage = refundResult.ErrorMessage };

        payment.Status        = newStatus;
        booking.PaymentStatus = newStatus;

        _db.TransactionLedger.Add(new TransactionLedger
        {
            Id                     = Guid.NewGuid(),
            PaymentId              = payment.Id,
            BookingId              = bookingId,
            UserId                 = booking.UserId,
            OwnerId                = booking.Court?.Venue?.OwnerId ?? Guid.Empty,
            EntryType              = LedgerEntryType.Refund,
            GrossAmount            = -eligibleRefundAmount,
            CommissionAmount       = -commissionReversed,
            NetAmount              = -ownerNetReversed,
            CommissionRateSnapshot = _commissionRate,
            Currency               = payment.Currency,
            Description            = $"Online {(isPartial ? "partial " : "")}refund via {_gateway.ProviderName}: {reason}",
            ProviderReference      = refundResult.RefundTransactionId,
            CreatedAt              = DateTime.UtcNow
        });

        if (cancellationFee > 0m)
        {
            _db.TransactionLedger.Add(new TransactionLedger
            {
                Id                     = Guid.NewGuid(),
                PaymentId              = payment.Id,
                BookingId              = bookingId,
                UserId                 = payment.Booking.UserId,
                OwnerId                = payment.Booking.Court?.Venue?.OwnerId ?? Guid.Empty,
                EntryType              = LedgerEntryType.CancellationFee,
                GrossAmount            = cancellationFee,
                CommissionAmount       = Math.Round(cancellationFee * _commissionRate, 2),
                NetAmount              = cancellationFee - Math.Round(cancellationFee * _commissionRate, 2),
                CommissionRateSnapshot = _commissionRate,
                Currency               = payment.Currency,
                Description            = $"Retained cancellation fee on refund: {reason}",
                CreatedAt              = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();

        if (_notifications is not null)
        {
            try
            {
                await _notifications.SendNotificationAsync(
                    payment.Booking.UserId,
                    "Refund Processed ↩️",
                    $"Refund of EGP {eligibleRefundAmount:0.00} has been processed for booking {payment.Booking.BookingReference}.",
                    NotificationType.PaymentRefunded,
                    $"/Bookings/Details?id={bookingId}");
            }
            catch { /* best effort */ }
        }

        return new RefundResponse
        {
            Success              = true,
            RefundTransactionId  = refundResult.RefundTransactionId,
            RefundAmount         = eligibleRefundAmount
        };
    }

    // ── Query Methods ───────────────────────────────────────────────────────

    public async Task<PaymentDetailsResponse?> GetPaymentByBookingAsync(Guid userId, Guid bookingId)
    {
        var payment = await _db.Payments
            .AsNoTracking()
            .Include(p => p.Booking)
            .FirstOrDefaultAsync(p => p.BookingId == bookingId && p.Booking.UserId == userId);

        if (payment is null) return null;

        return new PaymentDetailsResponse
        {
            Id                   = payment.Id,
            BookingId            = payment.BookingId,
            BookingReference     = payment.Booking.BookingReference,
            Amount               = payment.Amount,
            Currency             = payment.Currency,
            Method               = payment.Method.ToString(),
            Status               = payment.Status.ToString(),
            TransactionReference = payment.TransactionReference,
            ProviderOrderId      = payment.ProviderOrderId,
            PaidAt               = payment.PaidAt,
            CreatedAt            = payment.CreatedAt,
            ExpiresAt            = payment.ExpiresAt
        };
    }

    public async Task<PagedResult<TransactionLedgerDto>> GetAdminTransactionHistoryAsync(
        int page, int pageSize, string? status, DateTime? from, DateTime? to)
    {
        page     = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : (pageSize > 100 ? 100 : pageSize);

        var query = _db.TransactionLedger
            .AsNoTracking()
            .Include(t => t.Payment)
                .ThenInclude(p => p.Booking)
                    .ThenInclude(b => b.User)
            .Include(t => t.Payment)
                .ThenInclude(p => p.Booking)
                    .ThenInclude(b => b.Court)
                        .ThenInclude(c => c.Venue)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<LedgerEntryType>(status, true, out var entryType))
            query = query.Where(t => t.EntryType == entryType);

        if (from.HasValue)
            query = query.Where(t => t.CreatedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(t => t.CreatedAt <= to.Value);

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = items.Select(MapToLedgerDto).ToList();
        return PagedResult<TransactionLedgerDto>.From(dtos, total, page, pageSize);
    }

    public async Task<OwnerFinancialReportDto> GetOwnerFinancialReportAsync(
        Guid ownerId, DateTime? from, DateTime? to)
    {
        var baseQuery = _db.TransactionLedger
            .AsNoTracking()
            .Where(t => t.OwnerId == ownerId);

        if (from.HasValue) baseQuery = baseQuery.Where(t => t.CreatedAt >= from.Value);
        if (to.HasValue)   baseQuery = baseQuery.Where(t => t.CreatedAt <= to.Value);

        // SQL-side aggregations directly executed in database
        var totalGross = await baseQuery
            .Where(e => e.EntryType == LedgerEntryType.Payment)
            .SumAsync(e => (decimal?)e.GrossAmount) ?? 0m;

        var totalRefunds = Math.Abs(await baseQuery
            .Where(e => e.EntryType == LedgerEntryType.Refund)
            .SumAsync(e => (decimal?)e.GrossAmount) ?? 0m);

        var totalCancellationFees = await baseQuery
            .Where(e => e.EntryType == LedgerEntryType.CancellationFee)
            .SumAsync(e => (decimal?)e.GrossAmount) ?? 0m;

        // Platform commission: Payment commission (+) plus Refund commission reversal (-)
        var totalCommission = await baseQuery
            .Where(e => e.EntryType == LedgerEntryType.Payment || e.EntryType == LedgerEntryType.Refund)
            .SumAsync(e => (decimal?)e.CommissionAmount) ?? 0m;

        // Owner realized net: Payment net (+) plus Refund net reversal (-)
        // DOUBLE-COUNTING PREVENTION:
        // When a refund occurs, the unrefunded amount remains captured in Payment Net.
        // Payment Net + Refund Net already captures the exact retained owner earnings.
        // CancellationFee entries provide audit records and are reported in TotalCancellationFees,
        // but are NOT added again to TotalNet to prevent double-counting.
        var totalNet = await baseQuery
            .Where(e => e.EntryType == LedgerEntryType.Payment || e.EntryType == LedgerEntryType.Refund)
            .SumAsync(e => (decimal?)e.NetAmount) ?? 0m;

        var totalTransactions = await baseQuery.CountAsync();

        var entries = await baseQuery
            .Include(t => t.Payment)
                .ThenInclude(p => p.Booking)
                    .ThenInclude(b => b.User)
            .Include(t => t.Payment)
                .ThenInclude(p => p.Booking)
                    .ThenInclude(b => b.Court)
                        .ThenInclude(c => c.Venue)
            .OrderByDescending(t => t.CreatedAt)
            .Take(100) // safety limit for view
            .ToListAsync();

        return new OwnerFinancialReportDto
        {
            OwnerId               = ownerId,
            From                  = from,
            To                    = to,
            TotalGross            = totalGross,
            TotalCommission       = totalCommission,
            TotalNet              = totalNet,
            TotalRefunds          = totalRefunds,
            TotalCancellationFees = totalCancellationFees,
            TotalTransactions     = totalTransactions,
            Entries               = entries.Select(MapToLedgerDto).ToList()
        };
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task RecordIdempotencyAsync(string provider, string txId, Guid? paymentId, string action)
    {
        try
        {
            _db.IdempotencyLogs.Add(new IdempotencyLog
            {
                Id                    = Guid.NewGuid(),
                Provider              = provider,
                ProviderTransactionId = txId,
                ResponseStatusCode    = 200,
                Action                = action,
                PaymentId             = paymentId,
                ProcessedAt           = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record idempotency for {Provider}/{TxId}", provider, txId);
        }
    }

    private static TransactionLedgerDto MapToLedgerDto(TransactionLedger t) => new()
    {
        Id                     = t.Id,
        PaymentId              = t.PaymentId,
        BookingId              = t.BookingId,
        BookingReference       = t.Payment?.Booking?.BookingReference ?? string.Empty,
        EntryType              = t.EntryType.ToString(),
        GrossAmount            = t.GrossAmount,
        CommissionAmount       = t.CommissionAmount,
        NetAmount              = t.NetAmount,
        CommissionRateSnapshot = t.CommissionRateSnapshot,
        Currency               = t.Currency,
        Description            = t.Description,
        ProviderReference      = t.ProviderReference,
        CreatedAt              = t.CreatedAt,
        PlayerName             = t.Payment?.Booking?.User?.Name ?? string.Empty,
        VenueName              = t.Payment?.Booking?.Court?.Venue?.Name ?? string.Empty
    };
}

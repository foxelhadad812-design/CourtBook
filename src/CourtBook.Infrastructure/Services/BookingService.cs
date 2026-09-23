using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CourtBook.Infrastructure.Services
{
    public class BookingService : IBookingService
    {
        private readonly AppDbContext _db;
        private readonly INotificationService? _notificationService;
        private readonly IPaymentService? _paymentService;

        private const string BookingCourtLockResourcePrefix = "CourtBook:Booking:Court:";

        public BookingService(
            AppDbContext db,
            INotificationService? notificationService = null,
            IPaymentService? paymentService = null)
        {
            _db = db;
            _notificationService = notificationService;
            _paymentService = paymentService;
        }

        /// <summary>
        /// Creates a booking for the given court and time range.
        ///
        /// CONCURRENCY PROTECTION (two layers):
        ///
        /// Layer 1 — Application sp_getapplock:
        ///   Acquires an exclusive application lock on CourtBook:Booking:Court:{CourtId}
        ///   with LockOwner = 'Transaction' BEFORE the overlap check. This serializes
        ///   all booking operations for the same Court at the application level.
        ///   Same pattern as PaymentHoldWorker / SettlementWorker in this codebase.
        ///
        /// Layer 2 — Database trigger (TRG_Booking_NoOverlap):
        ///   The INSTEAD OF trigger also acquires sp_getapplock per CourtId,
        ///   checks for overlaps, and only allows the DML if no conflict exists.
        ///   This is the final backstop — even if the application lock is bypassed,
        ///   the database refuses overlapping writes.
        ///
        /// Blocking statuses: Pending, Confirmed, Completed, NoShow (all except Cancelled).
        /// </summary>
        public async Task<BookingResponse> CreateAsync(Guid userId, CreateBookingRequest request)
        {
            ValidateBookingRequest(request);

            var executionStrategy = _db.Database.CreateExecutionStrategy();

            return await executionStrategy.ExecuteAsync(async () =>
            {
                IDbContextTransaction? transaction = null;
                DbTransaction? dbTransaction = null;
                bool lockAcquired = false;

                if (_db.Database.IsRelational())
                {
                    var connection = _db.Database.GetDbConnection();
                    if (connection.State != ConnectionState.Open)
                        await connection.OpenAsync();

                    transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
                    dbTransaction = transaction.GetDbTransaction();

                    // Layer 1: acquire Court-level application lock BEFORE overlap check using active transaction
                    lockAcquired = await AcquireCourtLockAsync(connection, request.CourtId, dbTransaction);
                    if (!lockAcquired)
                    {
                        throw new InvalidOperationException(
                            "Court is not available for the selected time slot. Please try again later.");
                    }
                }

                try
                {
                    var court = await _db.Courts
                        .Include(c => c.Schedules)
                        .Include(c => c.Venue)
                            .ThenInclude(v => v.CancellationPolicy)
                        .Include(c => c.PriceRules.Where(pr => pr.IsActive))
                        .FirstOrDefaultAsync(c => c.Id == request.CourtId && c.IsActive);

                    if (court == null)
                        throw new ArgumentException("Court not found or inactive.");

                    // Validate against working schedule
                    var localStart = TimeZoneHelper.ConvertUtcToEgypt(request.StartTime);
                    var localEnd = TimeZoneHelper.ConvertUtcToEgypt(request.EndTime);
                    var dayOfWeek = localStart.DayOfWeek;
                    var schedule = court.Schedules.FirstOrDefault(s => s.DayOfWeek == dayOfWeek);
                    if (schedule == null)
                        throw new ArgumentException("Court is closed on this day.");

                    var requestStartTime = TimeOnly.FromDateTime(localStart);
                    var requestEndTime = TimeOnly.FromDateTime(localEnd);

                    if (requestStartTime < schedule.OpenTime || requestEndTime > schedule.CloseTime)
                        throw new ArgumentException("Booking time is outside court working hours.");

                    // Expired payment hold cleanup
                    var nowUtc = DateTime.UtcNow;
                    var expiredHoldBookings = await _db.Bookings
                        .Include(b => b.Payment)
                        .Where(b => b.CourtId == request.CourtId
                            && b.Status != BookingStatus.Cancelled
                            && b.Payment != null
                            && b.Payment.Status == PaymentStatus.Processing
                            && b.Payment.ExpiresAt != null
                            && b.Payment.ExpiresAt < nowUtc)
                        .ToListAsync();

                    if (expiredHoldBookings.Count > 0)
                    {
                        foreach (var exp in expiredHoldBookings)
                        {
                            exp.Status = BookingStatus.Cancelled;
                            exp.CancelledAt = nowUtc;
                            exp.CancellationReason = "Payment hold expired (10-minute checkout window elapsed).";
                            exp.Payment!.Status = PaymentStatus.Failed;
                            exp.PaymentStatus = PaymentStatus.Failed;
                        }
                        await _db.SaveChangesAsync();
                    }

                    // Layer 1: application overlap check (fast path)
                    var hasOverlap = await _db.Bookings
                        .AnyAsync(b => b.CourtId == request.CourtId
                            && b.Status != BookingStatus.Cancelled
                            && b.StartTime < request.EndTime
                            && b.EndTime > request.StartTime);

                    if (hasOverlap)
                        throw new InvalidOperationException("Court is not available for the selected time slot.");

                    // Check for conflicting community games
                    var requestDate = DateOnly.FromDateTime(localStart);
                    var hasGameConflict = await _db.Games
                        .AnyAsync(g => g.CourtId == request.CourtId
                            && (g.Status == GameStatus.Open || g.Status == GameStatus.Full)
                            && g.Date == requestDate
                            && g.StartTime < requestEndTime
                            && g.EndTime > requestStartTime);

                    if (hasGameConflict)
                        throw new InvalidOperationException("Court is reserved for a community match during this time slot.");

                    // Calculate price
                    var basePrice = court.PricePerHour * (decimal)(request.EndTime - request.StartTime).TotalHours;
                    var totalPrice = basePrice;

                    var matchingRule = court.PriceRules.FirstOrDefault(pr =>
                        (pr.DayOfWeek == null || pr.DayOfWeek == dayOfWeek) &&
                        requestStartTime >= pr.StartTime && requestEndTime <= pr.EndTime);

                    if (matchingRule != null)
                        totalPrice = matchingRule.FixedPrice ?? basePrice * matchingRule.PriceMultiplier;

                    var reference = $"PS-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}".Substring(0, 20).ToUpperInvariant();

                    var booking = new Booking
                    {
                        Id = Guid.NewGuid(),
                        BookingReference = reference,
                        CourtId = request.CourtId,
                        UserId = userId,
                        StartTime = request.StartTime,
                        EndTime = request.EndTime,
                        Status = BookingStatus.Confirmed,
                        PaymentStatus = PaymentStatus.Pending,
                        TotalPrice = Math.Round(totalPrice, 2),
                        Notes = request.Notes,
                        Court = court,
                        CreatedAt = DateTime.UtcNow
                    };

                    _db.Bookings.Add(booking);
                    await _db.SaveChangesAsync();

                    if (transaction != null)
                        await transaction.CommitAsync();

                    // Load user for notification
                    var user = await _db.Users.FindAsync(userId);
                    booking.User = user!;

                    // Send notifications
                    if (_notificationService != null)
                    {
                        await _notificationService.SendNotificationAsync(
                            userId,
                            "Booking Confirmed!",
                            $"Your booking {booking.BookingReference} at {court.Venue!.Name} (Court: {court.Name}) has been confirmed.",
                            NotificationType.BookingConfirmed,
                            $"/Bookings/Details?id={booking.Id}");

                        if (court.Venue != null && court.Venue.OwnerId != Guid.Empty && court.Venue.OwnerId != userId)
                        {
                            try
                            {
                                await _notificationService.SendNotificationAsync(
                                    court.Venue.OwnerId,
                                    "New Booking Received",
                                    $"New booking {booking.BookingReference} for {court.Name} by {user?.Name ?? "A player"}.",
                                    NotificationType.BookingConfirmed,
                                    "/owner/bookings");
                            }
                            catch { }
                        }
                    }

                    return MapToResponse(booking);
                }
                catch (DbUpdateException ex) when (IsBookingOverlapDatabaseError(ex))
                {
                    if (transaction != null)
                        await transaction.RollbackAsync();
                    throw new InvalidOperationException("Court is not available for the selected time slot.");
                }
                finally
                {
                    if (lockAcquired && _db.Database.IsRelational())
                    {
                        try
                        {
                            var conn = _db.Database.GetDbConnection();
                            if (conn.State == ConnectionState.Open)
                                await ReleaseCourtLockAsync(conn, request.CourtId, dbTransaction);
                        }
                        catch { /* best-effort release */ }
                    }
                    if (transaction != null)
                        await transaction.DisposeAsync();
                }
            });
        }

        public async Task<List<BookingResponse>> GetMyBookingsAsync(Guid userId)
        {
            var bookings = await _db.Bookings
                .AsNoTracking()
                .Include(b => b.Court)
                    .ThenInclude(c => c.Venue)
                        .ThenInclude(v => v.CancellationPolicy)
                .Include(b => b.User)
                .Include(b => b.Review)
                .Where(b => b.UserId == userId)
                .OrderByDescending(b => b.StartTime)
                .ToListAsync();

            return bookings.Select(MapToResponse).ToList();
        }

        public async Task<PagedResult<BookingResponse>> GetMyBookingsPagedAsync(
            Guid userId, BookingQueryRequest request)
        {
            var page = request.Page < 1 ? 1 : request.Page;
            var pageSize = request.PageSize < 1 ? 10 : Math.Min(request.PageSize, 50);
            var now = DateTime.UtcNow;

            var query = _db.Bookings
                .AsNoTracking()
                .Include(b => b.Court)
                    .ThenInclude(c => c.Venue)
                        .ThenInclude(v => v.CancellationPolicy)
                .Include(b => b.User)
                .Include(b => b.Review)
                .Where(b => b.UserId == userId);

            var statusFilter = (request.Status ?? "upcoming").Trim().ToLowerInvariant();
            if (statusFilter == "upcoming")
                query = query.Where(b => b.Status == BookingStatus.Confirmed && b.StartTime > now);
            else if (statusFilter == "completed")
                query = query.Where(b => b.Status == BookingStatus.Completed || (b.Status == BookingStatus.Confirmed && b.EndTime <= now));
            else if (statusFilter == "cancelled")
                query = query.Where(b => b.Status == BookingStatus.Cancelled);

            if (!string.IsNullOrWhiteSpace(request.Sport) &&
                Enum.TryParse<SportType>(request.Sport, true, out var sport))
                query = query.Where(b => b.Court.SportType == sport);

            query = statusFilter == "upcoming"
                ? query.OrderBy(b => b.StartTime)
                : query.OrderByDescending(b => b.StartTime);

            var totalCount = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return PagedResult<BookingResponse>.From(items.Select(MapToResponse).ToList(), totalCount, page, pageSize);
        }

        public async Task<BookingResponse?> GetByIdAsync(Guid userId, string userRole, Guid id)
        {
            var booking = await _db.Bookings
                .AsNoTracking()
                .Include(b => b.Court)
                    .ThenInclude(c => c.Venue)
                        .ThenInclude(v => v.CancellationPolicy)
                .Include(b => b.User)
                .Include(b => b.Review)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null) return null;

            // Authorization
            var isOwnerOfVenue = userRole == "Owner" && booking.Court?.Venue?.OwnerId == userId;
            var isAdmin = userRole == "Admin";
            var isBooker = booking.UserId == userId;

            if (!isBooker && !isOwnerOfVenue && !isAdmin)
                throw new UnauthorizedAccessException("You do not have permission to view this booking.");

            return MapToResponse(booking);
        }

        public async Task<CancellationPreviewResponse> GetCancellationPreviewAsync(
            Guid userId, string userRole, Guid id)
        {
            var booking = await _db.Bookings
                .AsNoTracking()
                .Include(b => b.Court)
                    .ThenInclude(c => c.Venue)
                        .ThenInclude(v => v.CancellationPolicy)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null)
                throw new KeyNotFoundException("Booking not found.");

            if (booking.UserId != userId && userRole != "Admin")
                throw new UnauthorizedAccessException("You do not have permission to access this booking.");

            var policy = booking.Court?.Venue?.CancellationPolicy;
            var freeHours = policy?.FreeCancellationHours ?? 24;
            var lateFeePercent = policy?.LateCancellationFeePercent ?? 50m;
            var policyDesc = policy?.PolicyDescription ??
                $"Free cancellation up to {freeHours} hours before start time.";

            var now = DateTime.UtcNow;
            var hoursUntilStart = (booking.StartTime - now).TotalHours;

            var preview = new CancellationPreviewResponse
            {
                BookingId = booking.Id,
                BookingReference = booking.BookingReference,
                VenueName = booking.Court?.Venue?.Name ?? "Sports Venue",
                CourtName = booking.Court?.Name ?? "Court",
                StartTime = booking.StartTime,
                HoursUntilStart = Math.Round(hoursUntilStart, 1),
                FreeCancellationHours = freeHours,
                LateCancellationFeePercent = lateFeePercent,
                TotalPrice = booking.TotalPrice,
                PolicyDescription = policyDesc
            };

            if (booking.Status == BookingStatus.Cancelled)
            {
                preview.CanCancel = false;
                preview.ReasonIfNotAllowed = "This booking is already cancelled.";
                return preview;
            }

            if (booking.Status == BookingStatus.Completed || booking.EndTime <= now)
            {
                preview.CanCancel = false;
                preview.ReasonIfNotAllowed = "Completed bookings cannot be cancelled.";
                return preview;
            }

            if (booking.StartTime <= now)
            {
                preview.CanCancel = false;
                preview.ReasonIfNotAllowed = "Booking time has already started.";
                return preview;
            }

            preview.CanCancel = true;
            if (hoursUntilStart >= freeHours)
            {
                preview.IsFreeCancellation = true;
                preview.CancellationFee = 0;
                preview.RefundAmount = booking.TotalPrice;
            }
            else
            {
                preview.IsFreeCancellation = false;
                preview.CancellationFee = Math.Round(booking.TotalPrice * (lateFeePercent / 100m), 2);
                preview.RefundAmount = Math.Max(0, booking.TotalPrice - preview.CancellationFee);
            }

            return preview;
        }

        public async Task<CancelBookingResult> CancelWithPolicyAsync(
            Guid userId, string userRole, Guid id, CancelBookingRequest? request)
        {
            var booking = await _db.Bookings
                .Include(b => b.Court)
                    .ThenInclude(c => c.Venue)
                        .ThenInclude(v => v.CancellationPolicy)
                .Include(b => b.Payment)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null)
                throw new KeyNotFoundException("Booking not found.");

            if (booking.UserId != userId && userRole != "Admin")
                throw new UnauthorizedAccessException("You do not have permission to cancel this booking.");

            if (booking.Status == BookingStatus.Cancelled)
                throw new InvalidOperationException("Booking is already cancelled.");

            var now = DateTime.UtcNow;
            if (booking.Status == BookingStatus.Completed || booking.EndTime <= now)
                throw new InvalidOperationException("Completed bookings cannot be cancelled.");

            if (booking.StartTime <= now)
                throw new InvalidOperationException("Booking has already started and cannot be cancelled.");

            var policy = booking.Court?.Venue?.CancellationPolicy;
            var freeHours = policy?.FreeCancellationHours ?? 24;
            var lateFeePercent = policy?.LateCancellationFeePercent ?? 50m;
            var hoursUntilStart = (booking.StartTime - now).TotalHours;

            decimal fee = 0;
            decimal refund = booking.TotalPrice;
            var isFree = hoursUntilStart >= freeHours;

            if (!isFree)
            {
                fee = Math.Round(booking.TotalPrice * (lateFeePercent / 100m), 2);
                refund = Math.Max(0, booking.TotalPrice - fee);
            }

            var reason = !string.IsNullOrWhiteSpace(request?.Reason)
                ? request.Reason
                : (isFree ? "Free cancellation by player"
                           : $"Late cancellation by player ({lateFeePercent}% fee applies: EGP {fee:0.00})");

            booking.Status = BookingStatus.Cancelled;
            booking.CancelledAt = now;
            booking.CancellationReason = reason;
            booking.CancellationFee = fee;

            // Financial state handling
            if (booking.Payment != null)
            {
                if (booking.Payment.Status == PaymentStatus.Completed)
                {
                    await _db.SaveChangesAsync();
                    if (_paymentService != null)
                    {
                        var refundResult = await _paymentService.ProcessRefundAsync(booking.Id, reason);
                        if (refundResult.Success)
                            refund = refundResult.RefundAmount;
                    }
                }
                else if (booking.Payment.Status == PaymentStatus.Processing || booking.Payment.Status == PaymentStatus.Pending)
                {
                    booking.Payment.Status = PaymentStatus.Cancelled;
                    booking.PaymentStatus = PaymentStatus.Cancelled;
                    await _db.SaveChangesAsync();
                    refund = 0m;
                }
                else
                {
                    booking.PaymentStatus = booking.Payment.Status;
                    await _db.SaveChangesAsync();
                    refund = 0m;
                }
            }
            else
            {
                await _db.SaveChangesAsync();
            }

            // Notifications
            if (_notificationService != null)
            {
                await _notificationService.SendNotificationAsync(
                    booking.UserId,
                    "Booking Cancelled",
                    $"Booking {booking.BookingReference} has been cancelled. " +
                    (isFree ? $"Full refund of EGP {refund:0.00}." : $"Fee: EGP {fee:0.00}, Refund: EGP {refund:0.00}."),
                    NotificationType.BookingCancelled,
                    $"/Bookings/Details?id={booking.Id}");

                if (booking.Court?.Venue?.OwnerId is { } ownerId
                    && ownerId != Guid.Empty
                    && ownerId != userId)
                {
                    try
                    {
                        await _notificationService.SendNotificationAsync(
                            ownerId,
                            "Booking Cancelled",
                            $"Booking {booking.BookingReference} for {booking.Court.Name} has been cancelled.",
                            NotificationType.BookingCancelled,
                            "/owner/bookings");
                    }
                    catch { }
                }
            }

            return new CancelBookingResult
            {
                Success = true,
                Message = isFree ? "Booking cancelled with full refund." :
                                   $"Booking cancelled with late fee of EGP {fee:0.00}.",
                CancellationFee = fee,
                RefundAmount = refund,
                CancelledAt = now
            };
        }

        public async Task<bool> CancelAsync(Guid userId, string userRole, Guid id)
        {
            var result = await CancelWithPolicyAsync(userId, userRole, id, null);
            return result.Success;
        }

        private static BookingResponse MapToResponse(Booking booking)
        {
            var now = DateTime.UtcNow;
            var effectiveStatus = booking.Status.ToString();

            // Auto-transition to Completed when past and was confirmed
            if (booking.Status == BookingStatus.Confirmed && booking.EndTime <= now)
                effectiveStatus = BookingStatus.Completed.ToString();

            var policy = booking.Court?.Venue?.CancellationPolicy;
            var freeHours = policy?.FreeCancellationHours ?? 24;
            var lateFeePercent = policy?.LateCancellationFeePercent ?? 50m;
            var policyDesc = policy?.PolicyDescription ?? $"Free cancellation up to {freeHours} hours before start time.";

            var canCancel = booking.Status == BookingStatus.Confirmed && booking.StartTime > now;
            var isCompleted = effectiveStatus == BookingStatus.Completed.ToString();
            var hasReviewed = booking.Review != null;
            var isEligibleForReview = isCompleted && !hasReviewed;

            return new BookingResponse
            {
                Id = booking.Id,
                BookingReference = booking.BookingReference,
                CourtId = booking.CourtId,
                CourtName = booking.Court?.Name ?? string.Empty,
                SportType = booking.Court?.SportType.ToString() ?? string.Empty,
                VenueId = booking.Court?.VenueId ?? Guid.Empty,
                VenueName = booking.Court?.Venue?.Name ?? string.Empty,
                VenueCity = booking.Court?.Venue?.City ?? string.Empty,
                VenueAddress = booking.Court?.Venue?.Address ?? string.Empty,
                VenuePhone = booking.Court?.Venue?.Phone ?? string.Empty,
                UserId = booking.UserId,
                UserName = booking.User?.Name ?? string.Empty,
                StartTime = booking.StartTime,
                EndTime = booking.EndTime,
                Status = effectiveStatus,
                PaymentStatus = booking.PaymentStatus.ToString(),
                TotalPrice = booking.TotalPrice,
                Notes = booking.Notes,
                CreatedAt = booking.CreatedAt,
                CancelledAt = booking.CancelledAt,
                CancellationReason = booking.CancellationReason,
                CanCancel = canCancel,
                FreeCancellationHours = freeHours,
                LateCancellationFeePercent = lateFeePercent,
                CancellationPolicyDescription = policyDesc,
                IsEligibleForReview = isEligibleForReview,
                HasReviewed = hasReviewed,
                ReviewId = booking.Review?.Id
            };
        }

        // ── Validation ──────────────────────────────────────────────────────────

        private static void ValidateBookingRequest(CreateBookingRequest request)
        {
            if (request.StartTime >= request.EndTime)
                throw new ArgumentException("StartTime must be before EndTime.");
            if (request.StartTime < DateTime.UtcNow.AddMinutes(-5))
                throw new ArgumentException("Cannot book in the past.");
        }

        // ── sp_getapplock helpers ────────────────────────────────────────────────

        /// <summary>
        /// Acquires an exclusive sp_getapplock for the given CourtId.
        /// LockOwner = 'Transaction' so the lock is automatically released on commit/rollback.
        /// </summary>
        private async Task<bool> AcquireCourtLockAsync(DbConnection connection, Guid courtId, DbTransaction? transaction = null)
        {
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            if (transaction != null)
                cmd.Transaction = transaction;

            cmd.CommandText = @"
                DECLARE @result INT;
                EXEC @result = sp_getapplock
                    @Resource = @ResourceName,
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction',
                    @LockTimeout = 5000;
                SELECT @result;";

            var resourceParam = cmd.CreateParameter();
            resourceParam.ParameterName = "@ResourceName";
            resourceParam.Value = BookingCourtLockResourcePrefix + courtId.ToString();
            cmd.Parameters.Add(resourceParam);

            var result = await cmd.ExecuteScalarAsync();
            var lockResult = result is int r ? r : -999;

            // 0 = granted synchronously, 1 = granted after wait, < 0 = denied/timeout/error
            return lockResult >= 0;
        }

        /// <summary>
        /// Releases the exclusive sp_getapplock for the given CourtId.
        /// Best-effort; the lock is also released automatically at transaction end.
        /// </summary>
        private async Task ReleaseCourtLockAsync(DbConnection connection, Guid courtId, DbTransaction? transaction = null)
        {
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            if (transaction != null && transaction.Connection != null)
                cmd.Transaction = transaction;

            cmd.CommandText = @"
                EXEC sp_releaseapplock
                    @Resource = @ResourceName,
                    @LockOwner = 'Transaction';";

            var resourceParam = cmd.CreateParameter();
            resourceParam.ParameterName = "@ResourceName";
            resourceParam.Value = BookingCourtLockResourcePrefix + courtId.ToString();
            cmd.Parameters.Add(resourceParam);

            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch { /* best-effort */ }
        }

        // ── Database error detection ─────────────────────────────────────────────

        /// <summary>
        /// Detects whether a DbUpdateException was caused by the TRG_Booking_NoOverlap trigger.
        ///
        /// The trigger RAISERRORs with severity 16, which SQL Server surfaces as a SqlException
        /// with Number == 50001. We check both the strongly-typed SqlException number AND fall
        /// back to message text comparison as a safety net.
        ///
        /// This is intentionally SQL Server-specific — the repository targets SQL Server only.
        /// </summary>
        private static bool IsBookingOverlapDatabaseError(DbUpdateException ex)
        {
            // Primary check: SQL Server error number 50001 (user-defined RAISERROR)
            if (ex.InnerException is SqlException sqlEx)
            {
                return sqlEx.Number == 50001;
            }

            // Fallback: message text contains the trigger's RAISERROR message
            // (covers edge cases where the exception is wrapped differently)
            return ex.InnerException is Exception inner &&
                   inner.Message.Contains("Court is not available for the selected time slot");
        }
    }
}

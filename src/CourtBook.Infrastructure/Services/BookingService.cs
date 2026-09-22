using System.Data;
using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CourtBook.Infrastructure.Services;

public class BookingService : IBookingService
{
    private readonly AppDbContext _db;
    private readonly INotificationService? _notificationService;
    private readonly IPaymentService? _paymentService;

    public BookingService(
        AppDbContext db,
        INotificationService? notificationService = null,
        IPaymentService? paymentService = null)
    {
        _db = db;
        _notificationService = notificationService;
        _paymentService = paymentService;
    }

    public async Task<BookingResponse> CreateAsync(Guid userId, CreateBookingRequest request)
    {
        if (request.StartTime >= request.EndTime)
            throw new ArgumentException("StartTime must be before EndTime.");

        if (request.StartTime < DateTime.UtcNow.AddMinutes(-5))
            throw new ArgumentException("Cannot book in the past.");

        var executionStrategy = _db.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            IDbContextTransaction? transaction = null;
            if (_db.Database.IsRelational())
            {
                transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            }

            try
            {
                var court = await _db.Courts
                    .Include(c => c.Schedules)
                    .Include(c => c.Venue)
                        .ThenInclude(v => v.CancellationPolicy)
                    .Include(c => c.PriceRules.Where(pr => pr.IsActive))
                    .FirstOrDefaultAsync(c => c.Id == request.CourtId && c.IsActive);

                if (court is null)
                    throw new ArgumentException("Court not found or inactive.");

                // Re-validate working schedule in Egypt local time
                var localStart = TimeZoneHelper.ConvertUtcToEgypt(request.StartTime);
                var localEnd = TimeZoneHelper.ConvertUtcToEgypt(request.EndTime);
                var dayOfWeek = localStart.DayOfWeek;
                var schedule = court.Schedules.FirstOrDefault(s => s.DayOfWeek == dayOfWeek);
                if (schedule is null)
                    throw new ArgumentException("Court is closed on this day.");

                var requestStartTime = TimeOnly.FromDateTime(localStart);
                var requestEndTime = TimeOnly.FromDateTime(localEnd);

                if (requestStartTime < schedule.OpenTime || requestEndTime > schedule.CloseTime)
                    throw new ArgumentException("Booking time is outside court working hours.");

                // Lazy cleanup: cancel any bookings on this court whose 10-minute online payment hold has expired
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

                foreach (var exp in expiredHoldBookings)
                {
                    exp.Status = BookingStatus.Cancelled;
                    exp.CancelledAt = nowUtc;
                    exp.CancellationReason = "Payment hold expired (10-minute checkout window elapsed).";
                    if (exp.Payment != null)
                    {
                        exp.Payment.Status = PaymentStatus.Failed;
                        exp.PaymentStatus = PaymentStatus.Failed;
                    }
                }
                if (expiredHoldBookings.Count > 0)
                {
                    await _db.SaveChangesAsync();
                }

                // Concurrency-safe overlap check executed inside the serializable transaction
                var hasOverlap = await _db.Bookings
                    .AnyAsync(b => b.CourtId == request.CourtId
                        && b.Status != BookingStatus.Cancelled
                        && b.StartTime < request.EndTime
                        && b.EndTime > request.StartTime);

                if (hasOverlap)
                    throw new InvalidOperationException("Court is not available for the selected time slot.");

                // Concurrency-safe check for conflicting active community games
                var requestDate = DateOnly.FromDateTime(localStart);
                var hasGameConflict = await _db.Games
                    .AnyAsync(g => g.CourtId == request.CourtId
                        && (g.Status == GameStatus.Open || g.Status == GameStatus.Full)
                        && g.Date == requestDate
                        && g.StartTime < requestEndTime
                        && g.EndTime > requestStartTime);

                if (hasGameConflict)
                    throw new InvalidOperationException("Court is reserved for a community match during this time slot.");

                // Calculate price with potential PriceRules
                var durationHours = (decimal)(request.EndTime - request.StartTime).TotalHours;
                var basePrice = court.PricePerHour * durationHours;
                var totalPrice = basePrice;

                var matchingRule = court.PriceRules.FirstOrDefault(pr =>
                    (pr.DayOfWeek == null || pr.DayOfWeek == dayOfWeek) &&
                    requestStartTime >= pr.StartTime && requestEndTime <= pr.EndTime);

                if (matchingRule is not null)
                {
                    totalPrice = matchingRule.FixedPrice ?? (basePrice * matchingRule.PriceMultiplier);
                }

                var reference = $"PS-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpper()}";

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

                if (transaction is not null)
                {
                    await transaction.CommitAsync();
                }

                var user = await _db.Users.FindAsync(userId);
                booking.User = user!;

                if (_notificationService is not null)
                {
                    await _notificationService.SendNotificationAsync(
                        userId,
                        "Booking Confirmed! 🎉",
                        $"Your booking {booking.BookingReference} at {court.Venue.Name} ({court.Name}) has been confirmed.",
                        NotificationType.BookingConfirmed,
                        $"/Bookings/Details?id={booking.Id}");

                    if (court.Venue != null && court.Venue.OwnerId != Guid.Empty && court.Venue.OwnerId != userId)
                    {
                        try
                        {
                            await _notificationService.SendNotificationAsync(
                                court.Venue.OwnerId,
                                "New Booking Received! 📅",
                                $"New booking {booking.BookingReference} for {court.Name} by {user?.Name ?? "Player"}.",
                                NotificationType.BookingConfirmed,
                                "/owner/bookings");
                        }
                        catch { }
                    }
                }

                return MapToResponse(booking);
            }
            catch
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync();
                }
                throw;
            }
            finally
            {
                if (transaction is not null)
                {
                    await transaction.DisposeAsync();
                }
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

    public async Task<PagedResult<BookingResponse>> GetMyBookingsPagedAsync(Guid userId, BookingQueryRequest request)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 10 : (request.PageSize > 50 ? 50 : request.PageSize);
        var now = DateTime.UtcNow;

        var query = _db.Bookings
            .AsNoTracking()
            .Include(b => b.Court)
                .ThenInclude(c => c.Venue)
                    .ThenInclude(v => v.CancellationPolicy)
            .Include(b => b.User)
            .Include(b => b.Review)
            .Where(b => b.UserId == userId);

        // Status Filter
        var statusFilter = (request.Status ?? "upcoming").Trim().ToLowerInvariant();
        if (statusFilter == "upcoming")
        {
            query = query.Where(b => b.Status == BookingStatus.Confirmed && b.StartTime > now);
        }
        else if (statusFilter == "completed")
        {
            query = query.Where(b => b.Status == BookingStatus.Completed || (b.Status == BookingStatus.Confirmed && b.EndTime <= now));
        }
        else if (statusFilter == "cancelled")
        {
            query = query.Where(b => b.Status == BookingStatus.Cancelled);
        }

        // Sport Filter
        if (!string.IsNullOrWhiteSpace(request.Sport) && Enum.TryParse<SportType>(request.Sport, true, out var sport))
        {
            query = query.Where(b => b.Court.SportType == sport);
        }

        // Order
        if (statusFilter == "upcoming")
        {
            query = query.OrderBy(b => b.StartTime);
        }
        else
        {
            query = query.OrderByDescending(b => b.StartTime);
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = items.Select(MapToResponse).ToList();
        return PagedResult<BookingResponse>.From(dtos, totalCount, page, pageSize);
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

        if (booking is null) return null;

        // Authorization check
        var isOwnerOfVenue = userRole == "Owner" && booking.Court?.Venue?.OwnerId == userId;
        var isAdmin = userRole == "Admin";
        var isBooker = booking.UserId == userId;

        if (!isBooker && !isOwnerOfVenue && !isAdmin)
            throw new UnauthorizedAccessException("You do not have permission to view this booking.");

        return MapToResponse(booking);
    }

    public async Task<CancellationPreviewResponse> GetCancellationPreviewAsync(Guid userId, string userRole, Guid id)
    {
        var booking = await _db.Bookings
            .AsNoTracking()
            .Include(b => b.Court)
                .ThenInclude(c => c.Venue)
                    .ThenInclude(v => v.CancellationPolicy)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (booking is null)
            throw new KeyNotFoundException("Booking not found.");

        if (booking.UserId != userId && userRole != "Admin")
            throw new UnauthorizedAccessException("You do not have permission to access this booking.");

        var policy = booking.Court?.Venue?.CancellationPolicy;
        var freeHours = policy?.FreeCancellationHours ?? 24;
        var lateFeePercent = policy?.LateCancellationFeePercent ?? 50m;
        var policyDesc = policy?.PolicyDescription ?? $"Free cancellation up to {freeHours} hours before start time. {lateFeePercent}% fee applies afterwards.";

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
            preview.ReasonIfNotAllowed = "Booking time has already started. Cancellations are not allowed.";
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

    public async Task<CancelBookingResult> CancelWithPolicyAsync(Guid userId, string userRole, Guid id, CancelBookingRequest? request)
    {
        var booking = await _db.Bookings
            .Include(b => b.Court)
                .ThenInclude(c => c.Venue)
                    .ThenInclude(v => v.CancellationPolicy)
            .Include(b => b.Payment)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (booking is null)
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
            : (isFree ? "Free cancellation by player" : $"Late cancellation by player ({lateFeePercent}% fee applies: EGP {fee:0.00})");

        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAt = now;
        booking.CancellationReason = reason;
        booking.CancellationFee = fee;

        // Financial state handling: Automatic refund or hold cancellation
        if (booking.Payment != null)
        {
            if (booking.Payment.Status == PaymentStatus.Completed)
            {
                // Save booking first so CancellationFee and Cancelled status are committed for the refund engine
                await _db.SaveChangesAsync();

                if (_paymentService != null)
                {
                    var refundResult = await _paymentService.ProcessRefundAsync(booking.Id, reason);
                    if (refundResult.Success)
                    {
                        refund = refundResult.RefundAmount;
                    }
                }
            }
            else if (booking.Payment.Status == PaymentStatus.Processing || booking.Payment.Status == PaymentStatus.Pending)
            {
                // Uncaptured online hold or unpaid pay-at-facility payment: cancel payment immediately without gateway call
                booking.Payment.Status = PaymentStatus.Cancelled;
                booking.PaymentStatus = PaymentStatus.Cancelled;
                await _db.SaveChangesAsync();
                refund = 0m;
            }
            else
            {
                // Already in a non-captured or refunded terminal state (Failed, Refunded, PartiallyRefunded, Cancelled)
                booking.PaymentStatus = booking.Payment.Status;
                await _db.SaveChangesAsync();
                refund = 0m;
            }
        }
        else
        {
            await _db.SaveChangesAsync();
        }

        if (_notificationService is not null)
        {
            await _notificationService.SendNotificationAsync(
                booking.UserId,
                "Booking Cancelled",
                $"Booking {booking.BookingReference} has been cancelled. {(isFree ? "Full refund of EGP " + refund : $"Fee: EGP {fee:0.00}, Refund: EGP {refund:0.00}")}.",
                NotificationType.BookingCancelled,
                $"/Bookings/Details?id={booking.Id}");

            if (booking.Court?.Venue?.OwnerId is not null && booking.Court.Venue.OwnerId != Guid.Empty && booking.Court.Venue.OwnerId != userId)
            {
                try
                {
                    await _notificationService.SendNotificationAsync(
                        booking.Court.Venue.OwnerId,
                        "Booking Cancelled ⚠️",
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
            Message = isFree ? "Booking cancelled with full refund." : $"Booking cancelled with late fee of EGP {fee:0.00}.",
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

        // Automatic transition to Completed if event is in the past and was confirmed
        if (booking.Status == BookingStatus.Confirmed && booking.EndTime <= now)
        {
            effectiveStatus = BookingStatus.Completed.ToString();
        }

        var policy = booking.Court?.Venue?.CancellationPolicy;
        var freeHours = policy?.FreeCancellationHours ?? 24;
        var lateFeePercent = policy?.LateCancellationFeePercent ?? 50m;
        var policyDesc = policy?.PolicyDescription ?? $"Free cancellation up to {freeHours} hours before start time.";

        var canCancel = booking.Status == BookingStatus.Confirmed && booking.StartTime > now;
        var isCompleted = effectiveStatus == BookingStatus.Completed.ToString();
        var hasReviewed = booking.Review is not null;
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

            // Cancellation info
            CancelledAt = booking.CancelledAt,
            CancellationReason = booking.CancellationReason,
            CanCancel = canCancel,
            FreeCancellationHours = freeHours,
            LateCancellationFeePercent = lateFeePercent,
            CancellationPolicyDescription = policyDesc,

            // Review info
            IsEligibleForReview = isEligibleForReview,
            HasReviewed = hasReviewed,
            ReviewId = booking.Review?.Id
        };
    }
}

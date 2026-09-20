using System.Data;
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

    public BookingService(AppDbContext db)
    {
        _db = db;
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
                    .Include(c => c.PriceRules.Where(pr => pr.IsActive))
                    .FirstOrDefaultAsync(c => c.Id == request.CourtId && c.IsActive);

                if (court is null)
                    throw new ArgumentException("Court not found or inactive.");

                // Re-validate working schedule
                var dayOfWeek = request.StartTime.DayOfWeek;
                var schedule = court.Schedules.FirstOrDefault(s => s.DayOfWeek == dayOfWeek);
                if (schedule is null)
                    throw new ArgumentException("Court is closed on this day.");

                var requestStartTime = TimeOnly.FromDateTime(request.StartTime);
                var requestEndTime = TimeOnly.FromDateTime(request.EndTime);

                if (requestStartTime < schedule.OpenTime || requestEndTime > schedule.CloseTime)
                    throw new ArgumentException("Booking time is outside court working hours.");

                // Concurrency-safe overlap check executed inside the serializable transaction
                var hasOverlap = await _db.Bookings
                    .AnyAsync(b => b.CourtId == request.CourtId
                        && b.Status != BookingStatus.Cancelled
                        && b.StartTime < request.EndTime
                        && b.EndTime > request.StartTime);

                if (hasOverlap)
                    throw new InvalidOperationException("Court is not available for the selected time slot.");

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
            .Include(b => b.User)
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.StartTime)
            .ToListAsync();

        return bookings.Select(MapToResponse).ToList();
    }

    public async Task<BookingResponse?> GetByIdAsync(Guid userId, string userRole, Guid id)
    {
        var booking = await _db.Bookings
            .AsNoTracking()
            .Include(b => b.Court)
                .ThenInclude(c => c.Venue)
            .Include(b => b.User)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (booking is null) return null;

        if (booking.UserId != userId && userRole != "Admin")
            throw new UnauthorizedAccessException("Not your booking.");

        return MapToResponse(booking);
    }

    public async Task<bool> CancelAsync(Guid userId, string userRole, Guid id)
    {
        var booking = await _db.Bookings.FindAsync(id);
        if (booking is null) return false;

        if (booking.UserId != userId && userRole != "Admin")
            throw new UnauthorizedAccessException("Not your booking.");

        if (booking.Status == BookingStatus.Cancelled)
            return true; // Already cancelled

        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAt = DateTime.UtcNow;
        booking.CancellationReason = "Cancelled by user";
        await _db.SaveChangesAsync();
        return true;
    }

    private static BookingResponse MapToResponse(Booking booking)
    {
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
            UserId = booking.UserId,
            UserName = booking.User?.Name ?? string.Empty,
            StartTime = booking.StartTime,
            EndTime = booking.EndTime,
            Status = booking.Status.ToString(),
            PaymentStatus = booking.PaymentStatus.ToString(),
            TotalPrice = booking.TotalPrice,
            Notes = booking.Notes,
            CreatedAt = booking.CreatedAt
        };
    }
}

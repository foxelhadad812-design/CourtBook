using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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

        if (request.StartTime < DateTime.UtcNow)
            throw new ArgumentException("Cannot book in the past.");

        var court = await _db.Courts
            .Include(c => c.Schedules)
            .FirstOrDefaultAsync(c => c.Id == request.CourtId && c.IsActive);

        if (court is null)
            throw new ArgumentException("Court not found or inactive.");

        // Check schedule
        var dayOfWeek = request.StartTime.DayOfWeek;
        var schedule = court.Schedules.FirstOrDefault(s => s.DayOfWeek == dayOfWeek);
        if (schedule is null)
            throw new ArgumentException("Court is closed on this day.");

        var requestStartTime = TimeOnly.FromDateTime(request.StartTime);
        var requestEndTime = TimeOnly.FromDateTime(request.EndTime);

        if (requestStartTime < schedule.OpenTime || requestEndTime > schedule.CloseTime)
            throw new ArgumentException("Booking time is outside court working hours.");

        // Check for overlapping bookings
        var hasOverlap = await _db.Bookings
            .AnyAsync(b => b.CourtId == request.CourtId
                && b.Status != BookingStatus.Cancelled
                && b.StartTime < request.EndTime
                && b.EndTime > request.StartTime);

        if (hasOverlap)
            throw new InvalidOperationException("Court is not available for the selected time slot");

        var durationHours = (request.EndTime - request.StartTime).TotalHours;
        var totalPrice = (decimal)durationHours * court.PricePerHour;

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            CourtId = request.CourtId,
            UserId = userId,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Status = BookingStatus.Confirmed,
            TotalPrice = totalPrice
        };

        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        return MapToResponse(booking);
    }

    public async Task<List<BookingResponse>> GetMyBookingsAsync(Guid userId)
    {
        var bookings = await _db.Bookings
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.StartTime)
            .ToListAsync();

        return bookings.Select(MapToResponse).ToList();
    }

    public async Task<BookingResponse?> GetByIdAsync(Guid userId, string userRole, Guid id)
    {
        var booking = await _db.Bookings.FindAsync(id);
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
        await _db.SaveChangesAsync();
        return true;
    }

    private static BookingResponse MapToResponse(Booking booking)
    {
        return new BookingResponse
        {
            Id = booking.Id,
            CourtId = booking.CourtId,
            UserId = booking.UserId,
            StartTime = booking.StartTime,
            EndTime = booking.EndTime,
            Status = booking.Status.ToString(),
            TotalPrice = booking.TotalPrice,
            CreatedAt = booking.CreatedAt
        };
    }
}

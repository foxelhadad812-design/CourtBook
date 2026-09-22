using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Infrastructure.Services;

public class AvailabilityService : IAvailabilityService
{
    private readonly AppDbContext _db;

    public AvailabilityService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<CourtAvailabilityResponse>> GetCourtAvailabilityAsync(Guid courtId, CourtAvailabilityRequest request)
    {
        if (request.DurationMinutes < 30 || request.DurationMinutes > 720)
            return Error.Validation("Duration must be between 30 and 720 minutes.");

        var court = await _db.Courts
            .AsNoTracking()
            .Include(c => c.Venue)
                .ThenInclude(v => v.CancellationPolicy)
            .Include(c => c.Schedules)
            .Include(c => c.PriceRules.Where(pr => pr.IsActive))
            .FirstOrDefaultAsync(c => c.Id == courtId);

        if (court is null || !court.IsActive)
            return Error.NotFound("Court");

        var dayOfWeek = request.Date.DayOfWeek;
        var schedule = court.Schedules.FirstOrDefault(s => s.DayOfWeek == dayOfWeek);

        var cancellationPolicyDesc = court.Venue.CancellationPolicy?.PolicyDescription 
            ?? "Free cancellation up to 24 hours before your booking.";
        var freeCancellationHours = court.Venue.CancellationPolicy?.FreeCancellationHours ?? 24;

        var response = new CourtAvailabilityResponse
        {
            CourtId = court.Id,
            CourtName = court.Name,
            SportType = court.SportType.ToString(),
            VenueId = court.VenueId,
            VenueName = court.Venue.Name,
            VenueCity = court.Venue.City,
            Date = request.Date,
            DurationMinutes = request.DurationMinutes,
            FreeCancellationHours = freeCancellationHours,
            CancellationPolicyDescription = cancellationPolicyDesc,
            Slots = []
        };

        // If court is closed on this day, return empty slots
        if (schedule is null || schedule.OpenTime >= schedule.CloseTime)
            return Result<CourtAvailabilityResponse>.Ok(response);

        // Fetch active bookings and active community games for this day in Egypt timezone
        var (dayStartUtc, dayEndUtc) = TimeZoneHelper.GetEgyptDayUtcRange(request.Date);

        var nowUtc = DateTime.UtcNow;

        var existingBookings = await _db.Bookings
            .AsNoTracking()
            .Where(b => b.CourtId == courtId 
                     && b.Status != BookingStatus.Cancelled
                     && !(b.Payment != null && b.Payment.Status == PaymentStatus.Processing && b.Payment.ExpiresAt != null && b.Payment.ExpiresAt < nowUtc)
                     && b.StartTime < dayEndUtc 
                     && b.EndTime > dayStartUtc)
            .Select(b => new { b.StartTime, b.EndTime })
            .ToListAsync();

        var existingGames = await _db.Games
            .AsNoTracking()
            .Where(g => g.CourtId == courtId
                     && (g.Status == GameStatus.Open || g.Status == GameStatus.Full)
                     && g.Date == request.Date)
            .Select(g => new { g.StartTime, g.EndTime })
            .ToListAsync();
        var currentSlotStart = schedule.OpenTime;
        var slotIncrement = request.DurationMinutes;

        while (true)
        {
            // Calculate slot end time
            var currentSlotEnd = currentSlotStart.AddMinutes(slotIncrement);
            
            // If the slot extends past closing or wraps past midnight, finish
            if (currentSlotEnd > schedule.CloseTime || currentSlotEnd < currentSlotStart)
                break;

            var slotStartUtc = TimeZoneHelper.CreateUtcFromEgyptDateAndTime(request.Date, currentSlotStart);
            var slotEndUtc = TimeZoneHelper.CreateUtcFromEgyptDateAndTime(request.Date, currentSlotEnd);

            // Determine status
            SlotAvailabilityStatus status;
            if (slotStartUtc < nowUtc)
            {
                status = SlotAvailabilityStatus.Past;
            }
            else
            {
                var isBookingOverlap = existingBookings.Any(b => b.StartTime < slotEndUtc && b.EndTime > slotStartUtc);
                var isGameOverlap = existingGames.Any(g => g.StartTime < currentSlotEnd && g.EndTime > currentSlotStart);
                status = (isBookingOverlap || isGameOverlap) ? SlotAvailabilityStatus.Booked : SlotAvailabilityStatus.Available;
            }

            // Calculate pricing
            var durationRatio = (decimal)request.DurationMinutes / 60.0m;
            var basePrice = court.PricePerHour * durationRatio;
            var effectivePrice = basePrice;
            var isPeak = false;
            string? appliedRuleName = null;

            var matchingRule = court.PriceRules.FirstOrDefault(pr =>
                (pr.DayOfWeek == null || pr.DayOfWeek == dayOfWeek) &&
                currentSlotStart >= pr.StartTime && currentSlotEnd <= pr.EndTime);

            if (matchingRule is not null)
            {
                isPeak = true;
                appliedRuleName = matchingRule.Name;
                effectivePrice = matchingRule.FixedPrice ?? (basePrice * matchingRule.PriceMultiplier);
            }

            response.Slots.Add(new AvailabilitySlotDto
            {
                StartTime = currentSlotStart,
                EndTime = currentSlotEnd,
                Status = status,
                BasePrice = Math.Round(basePrice, 2),
                EffectivePrice = Math.Round(effectivePrice, 2),
                IsPeak = isPeak,
                AppliedRuleName = appliedRuleName
            });

            // Advance to next slot
            currentSlotStart = currentSlotEnd;
        }

        return Result<CourtAvailabilityResponse>.Ok(response);
    }
}

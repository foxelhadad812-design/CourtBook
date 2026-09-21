using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Infrastructure.Services;

public class CourtService : ICourtService
{
    private readonly AppDbContext _db;

    public CourtService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<CourtResponse>> GetAllByVenueAsync(Guid venueId)
    {
        var courts = await _db.Courts
            .Include(c => c.Schedules)
            .Where(c => c.VenueId == venueId)
            .ToListAsync();

        return courts.Select(MapToResponse).ToList();
    }

    public async Task<CourtResponse?> GetByIdAsync(Guid venueId, Guid id)
    {
        var court = await _db.Courts
            .Include(c => c.Schedules)
            .FirstOrDefaultAsync(c => c.VenueId == venueId && c.Id == id);

        return court is null ? null : MapToResponse(court);
    }

    public async Task<CourtResponse?> GetByIdAsync(Guid id)
    {
        var court = await _db.Courts
            .Include(c => c.Schedules)
            .Include(c => c.Venue)
            .FirstOrDefaultAsync(c => c.Id == id);

        return court is null ? null : MapToResponse(court);
    }

    public async Task<CourtResponse?> CreateAsync(Guid ownerId, Guid venueId, CreateCourtRequest request)
    {
        var venue = await _db.Venues.FindAsync(venueId);
        if (venue is null) return null;
        if (venue.OwnerId != ownerId) throw new UnauthorizedAccessException("Not your venue.");

        if (!Enum.TryParse<SportType>(request.SportType, true, out var sportType))
        {
            throw new ArgumentException("Invalid SportType.");
        }

        var court = new Court
        {
            Id = Guid.NewGuid(),
            VenueId = venueId,
            Name = request.Name,
            SportType = sportType,
            PricePerHour = request.PricePerHour,
            IsActive = request.IsActive
        };

        _db.Courts.Add(court);
        await _db.SaveChangesAsync();

        return MapToResponse(court);
    }

    public async Task<CourtResponse?> UpdateAsync(Guid ownerId, Guid venueId, Guid id, UpdateCourtRequest request)
    {
        var court = await _db.Courts.Include(c => c.Venue).Include(c => c.Schedules).FirstOrDefaultAsync(c => c.Id == id && c.VenueId == venueId);
        if (court is null) return null;

        if (court.Venue.OwnerId != ownerId) throw new UnauthorizedAccessException("Not your venue.");

        if (!Enum.TryParse<SportType>(request.SportType, true, out var sportType))
        {
            throw new ArgumentException("Invalid SportType.");
        }

        court.Name = request.Name;
        court.SportType = sportType;
        court.PricePerHour = request.PricePerHour;
        court.IsActive = request.IsActive;

        await _db.SaveChangesAsync();
        return MapToResponse(court);
    }

    public async Task<bool> DeleteAsync(Guid userId, string userRole, Guid venueId, Guid id)
    {
        var court = await _db.Courts.Include(c => c.Venue).FirstOrDefaultAsync(c => c.Id == id && c.VenueId == venueId);
        if (court is null) return false;

        if (court.Venue.OwnerId != userId && userRole != "Admin")
            throw new UnauthorizedAccessException("Not your venue.");

        var nowUtc = DateTime.UtcNow;
        var hasUpcomingBookings = await _db.Bookings
            .AnyAsync(b => b.CourtId == id && b.Status == BookingStatus.Confirmed && b.EndTime > nowUtc);

        var todayDate = DateOnly.FromDateTime(TimeZoneHelper.GetCurrentEgyptTime());
        var currentTime = TimeOnly.FromDateTime(TimeZoneHelper.GetCurrentEgyptTime());
        var hasActiveGames = await _db.Games
            .AnyAsync(g => g.CourtId == id
                && (g.Status == GameStatus.Open || g.Status == GameStatus.Full)
                && (g.Date > todayDate || (g.Date == todayDate && g.EndTime > currentTime)));

        if (hasUpcomingBookings || hasActiveGames)
        {
            throw new InvalidOperationException("Cannot delete court with active upcoming bookings or community games. Please cancel them first.");
        }

        var hasHistoricalBookings = await _db.Bookings.AnyAsync(b => b.CourtId == id);
        var hasHistoricalGames = await _db.Games.AnyAsync(g => g.CourtId == id);

        if (hasHistoricalBookings || hasHistoricalGames)
        {
            court.IsActive = false;
            await _db.SaveChangesAsync();
            return true;
        }

        _db.Courts.Remove(court);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<ScheduleResponse?> AddScheduleAsync(Guid ownerId, Guid venueId, Guid courtId, CreateScheduleRequest request)
    {
        var court = await _db.Courts.Include(c => c.Venue).FirstOrDefaultAsync(c => c.Id == courtId && c.VenueId == venueId);
        if (court is null) return null;
        if (court.Venue.OwnerId != ownerId) throw new UnauthorizedAccessException("Not your venue.");

        var schedule = new CourtSchedule
        {
            Id = Guid.NewGuid(),
            CourtId = courtId,
            DayOfWeek = (DayOfWeek)request.DayOfWeek,
            OpenTime = TimeOnly.Parse(request.OpenTime),
            CloseTime = TimeOnly.Parse(request.CloseTime)
        };

        _db.CourtSchedules.Add(schedule);
        await _db.SaveChangesAsync();

        return new ScheduleResponse
        {
            Id = schedule.Id,
            CourtId = schedule.CourtId,
            DayOfWeek = (int)schedule.DayOfWeek,
            OpenTime = schedule.OpenTime.ToString("HH:mm"),
            CloseTime = schedule.CloseTime.ToString("HH:mm")
        };
    }

    public async Task<bool> DeleteScheduleAsync(Guid ownerId, Guid venueId, Guid courtId, Guid scheduleId)
    {
        var schedule = await _db.CourtSchedules.Include(s => s.Court).ThenInclude(c => c.Venue)
            .FirstOrDefaultAsync(s => s.Id == scheduleId && s.CourtId == courtId && s.Court.VenueId == venueId);
            
        if (schedule is null) return false;
        if (schedule.Court.Venue.OwnerId != ownerId) throw new UnauthorizedAccessException("Not your venue.");

        _db.CourtSchedules.Remove(schedule);
        await _db.SaveChangesAsync();
        return true;
    }

    private static CourtResponse MapToResponse(Court court)
    {
        return new CourtResponse
        {
            Id = court.Id,
            VenueId = court.VenueId,
            Name = court.Name,
            Description = court.Description,
            SportType = court.SportType.ToString(),
            PricePerHour = court.PricePerHour,
            SurfaceType = court.SurfaceType,
            IsIndoor = court.IsIndoor,
            Capacity = court.Capacity,
            IsActive = court.IsActive,
            Schedules = court.Schedules?.Select(s => new ScheduleResponse
            {
                Id = s.Id,
                CourtId = s.CourtId,
                DayOfWeek = (int)s.DayOfWeek,
                OpenTime = s.OpenTime.ToString("HH:mm"),
                CloseTime = s.CloseTime.ToString("HH:mm")
            }).ToList() ?? new List<ScheduleResponse>()
        };
    }
}

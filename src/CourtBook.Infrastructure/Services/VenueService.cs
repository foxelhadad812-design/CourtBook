using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Infrastructure.Services;

public class VenueService : IVenueService
{
    private readonly AppDbContext _db;

    public VenueService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<VenueResponse>> GetAllAsync()
    {
        var venues = await _db.Venues
            .Include(v => v.Courts)
            .ToListAsync();

        return venues.Select(MapToResponse).ToList();
    }

    public async Task<VenueResponse?> GetByIdAsync(Guid id)
    {
        var venue = await _db.Venues
            .Include(v => v.Courts)
                .ThenInclude(c => c.Schedules)
            .FirstOrDefaultAsync(v => v.Id == id);

        return venue is null ? null : MapToResponse(venue);
    }

    public async Task<VenueResponse> CreateAsync(Guid ownerId, CreateVenueRequest request)
    {
        var venue = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Name = request.Name,
            City = request.City,
            Address = request.Address
        };

        _db.Venues.Add(venue);
        await _db.SaveChangesAsync();

        return MapToResponse(venue);
    }

    public async Task<VenueResponse?> UpdateAsync(Guid ownerId, string ownerRole, Guid id, UpdateVenueRequest request)
    {
        var venue = await _db.Venues.Include(v => v.Courts).FirstOrDefaultAsync(v => v.Id == id);
        if (venue is null) return null;

        if (venue.OwnerId != ownerId && ownerRole != "Admin")
            throw new UnauthorizedAccessException("You don't have permission to update this venue.");

        venue.Name = request.Name;
        venue.City = request.City;
        venue.Address = request.Address;

        await _db.SaveChangesAsync();
        return MapToResponse(venue);
    }

    public async Task<bool> DeleteAsync(Guid userId, string userRole, Guid id)
    {
        var venue = await _db.Venues.FindAsync(id);
        if (venue is null) return false;

        if (venue.OwnerId != userId && userRole != "Admin")
            throw new UnauthorizedAccessException("You don't have permission to delete this venue.");

        _db.Venues.Remove(venue);
        await _db.SaveChangesAsync();
        return true;
    }

    private static VenueResponse MapToResponse(Venue venue)
    {
        return new VenueResponse
        {
            Id = venue.Id,
            OwnerId = venue.OwnerId,
            Name = venue.Name,
            City = venue.City,
            Address = venue.Address,
            Courts = venue.Courts.Select(c => new CourtResponse
            {
                Id = c.Id,
                VenueId = c.VenueId,
                Name = c.Name,
                SportType = c.SportType.ToString(),
                PricePerHour = c.PricePerHour,
                IsActive = c.IsActive,
                Schedules = c.Schedules?.Select(s => new ScheduleResponse
                {
                    Id = s.Id,
                    CourtId = s.CourtId,
                    DayOfWeek = (int)s.DayOfWeek,
                    OpenTime = s.OpenTime.ToString("HH:mm"),
                    CloseTime = s.CloseTime.ToString("HH:mm")
                }).ToList() ?? new List<ScheduleResponse>()
            }).ToList()
        };
    }
}

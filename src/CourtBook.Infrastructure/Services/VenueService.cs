using CourtBook.Application.Common;
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

    public async Task<PagedResult<VenueCardDto>> SearchAsync(VenueSearchRequest request)
    {
        var query = _db.Venues
            .AsNoTracking()
            .Where(v => v.IsActive);

        // Filter: Full-text search
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(v => v.Name.ToLower().Contains(term) 
                                  || v.City.ToLower().Contains(term) 
                                  || v.Area.ToLower().Contains(term));
        }

        // Filter: City
        if (!string.IsNullOrWhiteSpace(request.City))
        {
            query = query.Where(v => v.City.ToLower().Contains(request.City.Trim().ToLower()));
        }

        // Filter: Area
        if (!string.IsNullOrWhiteSpace(request.Area))
        {
            query = query.Where(v => v.Area.ToLower().Contains(request.Area.Trim().ToLower()));
        }

        // Filter: Sport
        if (!string.IsNullOrWhiteSpace(request.Sport))
        {
            var sportTerm = request.Sport.Trim().ToLower();
            query = query.Where(v => v.Courts.Any(c => c.IsActive && c.SportType.ToString().ToLower() == sportTerm));
        }

        // Filter: MinPrice & MaxPrice
        if (request.MinPrice.HasValue)
        {
            query = query.Where(v => v.Courts.Any(c => c.IsActive && c.PricePerHour >= request.MinPrice.Value));
        }
        if (request.MaxPrice.HasValue)
        {
            query = query.Where(v => v.Courts.Any(c => c.IsActive && c.PricePerHour <= request.MaxPrice.Value));
        }

        // Filter: Rating
        if (request.MinRating.HasValue)
        {
            query = query.Where(v => v.AverageRating >= request.MinRating.Value);
        }

        // Filter: Amenities
        if (request.AmenityIds is not null && request.AmenityIds.Count > 0)
        {
            query = query.Where(v => v.Amenities.Any(a => request.AmenityIds.Contains(a.AmenityId)));
        }

        // Sorting
        query = (request.SortBy?.ToLower()) switch
        {
            "price_asc"   => query.OrderBy(v => v.Courts.Where(c => c.IsActive).Min(c => (decimal?)c.PricePerHour) ?? 0),
            "price_desc"  => query.OrderByDescending(v => v.Courts.Where(c => c.IsActive).Max(c => (decimal?)c.PricePerHour) ?? 0),
            "rating_desc" => query.OrderByDescending(v => v.AverageRating).ThenByDescending(v => v.TotalReviews),
            "newest"      => query.OrderByDescending(v => v.CreatedAt),
            "name"        => query.OrderBy(v => v.Name),
            _             => query.OrderByDescending(v => v.AverageRating).ThenByDescending(v => v.TotalReviews)
        };

        var totalCount = await query.CountAsync();

        // Single projected query to avoid N+1 issues
        var items = await query
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(v => new VenueCardDto
            {
                Id = v.Id,
                Name = v.Name,
                Description = v.Description,
                City = v.City,
                Area = v.Area,
                Address = v.Address,
                Latitude = v.Latitude,
                Longitude = v.Longitude,
                AverageRating = v.AverageRating,
                TotalReviews = v.TotalReviews,
                IsVerified = v.IsVerified,
                CourtsCount = v.Courts.Count(c => c.IsActive),
                StartingPrice = v.Courts.Where(c => c.IsActive).Min(c => (decimal?)c.PricePerHour) ?? 0,
                Sports = v.Courts.Where(c => c.IsActive).Select(c => c.SportType.ToString()).Distinct().ToList(),
                Amenities = v.Amenities.Select(a => a.Amenity.Name).ToList(),
                PrimaryImageUrl = v.Images.Where(i => i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault()
            })
            .ToListAsync();

        return PagedResult<VenueCardDto>.From(items, totalCount, request.Page, request.PageSize);
    }

    public async Task<List<VenueResponse>> GetAllAsync()
    {
        var venues = await _db.Venues
            .AsNoTracking()
            .Include(v => v.Courts)
            .ToListAsync();

        return venues.Select(MapToResponse).ToList();
    }

    public async Task<VenueResponse?> GetByIdAsync(Guid id)
    {
        var venue = await _db.Venues
            .AsNoTracking()
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
            Address = request.Address,
            CreatedAt = DateTime.UtcNow
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

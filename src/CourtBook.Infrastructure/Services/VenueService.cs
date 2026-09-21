using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
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
            .Where(v => v.IsActive && v.ApprovalStatus == VenueApprovalStatus.Approved);

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

        // Filter: Sport (by actual active court relation)
        if (!string.IsNullOrWhiteSpace(request.Sport))
        {
            if (Enum.TryParse<SportType>(request.Sport.Trim(), true, out var parsedSport))
            {
                query = query.Where(v => v.Courts.Any(c => c.IsActive && c.SportType == parsedSport));
            }
            else
            {
                var sportTerm = request.Sport.Trim().ToLower();
                query = query.Where(v => v.Courts.Any(c => c.IsActive && c.SportType.ToString().ToLower() == sportTerm));
            }
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

        // Use AsSplitQuery with eager loading to avoid subquery translation issues in SQL Server
        var venues = await query
            .Include(v => v.Courts.Where(c => c.IsActive))
            .Include(v => v.Amenities)
                .ThenInclude(a => a.Amenity)
            .Include(v => v.Images.Where(i => i.IsPrimary))
            .AsSplitQuery()
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToListAsync();

        var items = venues.Select(v => new VenueCardDto
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
            SportsSummary = v.Courts.Where(c => c.IsActive)
                .GroupBy(c => c.SportType.ToString())
                .Select(g => new SportCountDto { Sport = g.Key, CourtCount = g.Count() })
                .ToList(),
            Amenities = v.Amenities.Select(a => a.Amenity?.Name ?? "").Where(name => !string.IsNullOrEmpty(name)).ToList(),
            PrimaryImageUrl = v.Images.FirstOrDefault(i => i.IsPrimary)?.ImageUrl
        }).ToList();

        return PagedResult<VenueCardDto>.From(items, totalCount, request.Page, request.PageSize);
    }

    public async Task<List<VenueResponse>> GetAllAsync()
    {
        var venues = await _db.Venues
            .AsNoTracking()
            .Where(v => v.IsActive && v.ApprovalStatus == VenueApprovalStatus.Approved)
            .Include(v => v.Courts.Where(c => c.IsActive))
            .Include(v => v.Amenities).ThenInclude(va => va.Amenity)
            .Include(v => v.Images)
            .AsSplitQuery()
            .ToListAsync();

        return venues.Select(MapToResponse).ToList();
    }

    public async Task<VenueResponse?> GetByIdAsync(Guid id)
    {
        var venue = await _db.Venues
            .AsNoTracking()
            .Include(v => v.Courts.Where(c => c.IsActive))
                .ThenInclude(c => c.Schedules)
            .Include(v => v.Amenities)
                .ThenInclude(va => va.Amenity)
            .Include(v => v.Images.OrderBy(i => i.DisplayOrder))
            .Include(v => v.OperatingHours.OrderBy(o => o.DayOfWeek))
            .Include(v => v.CancellationPolicy)
            .AsSplitQuery()
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
        var sports = venue.Courts
            .Where(c => c.IsActive)
            .Select(c => c.SportType.ToString())
            .Distinct()
            .ToList();

        // If no images seeded in DB, generate default photo items from wwwroot/images
        var images = venue.Images != null && venue.Images.Any()
            ? venue.Images.OrderBy(i => i.DisplayOrder).Select(i => new VenueImageDto
            {
                Id = i.Id,
                ImageUrl = i.ImageUrl,
                IsPrimary = i.IsPrimary,
                DisplayOrder = i.DisplayOrder,
                Caption = i.Caption
            }).ToList()
            : sports.Select((sp, idx) => new VenueImageDto
            {
                Id = Guid.NewGuid(),
                ImageUrl = sp.ToLower() switch
                {
                    "football" => "/images/venues/football/football-pitch-01.jpg",
                    "padel" => "/images/venues/padel/padel-court-01.jpg",
                    "tennis" => "/images/venues/tennis/tennis-clay-01.jpg",
                    "basketball" => "/images/venues/basketball/basketball-indoor-01.jpg",
                    "volleyball" => "/images/venues/volleyball/volleyball-indoor-01.jpg",
                    "badminton" => "/images/venues/badminton/badminton-court-01.jpg",
                    _ => "/images/venues/facilities/complex-exterior-01.jpg"
                },
                IsPrimary = idx == 0,
                DisplayOrder = idx,
                Caption = $"{venue.Name} - {sp} Court"
            }).ToList();

        // If no operating hours seeded in DB, fall back to standard 08:00 - 23:00 daily
        var opHours = venue.OperatingHours != null && venue.OperatingHours.Any()
            ? venue.OperatingHours.OrderBy(o => o.DayOfWeek).Select(o => new OperatingHourDto
            {
                DayOfWeek = o.DayOfWeek,
                DayName = o.DayOfWeek.ToString(),
                OpenTime = o.OpenTime.ToString("HH:mm"),
                CloseTime = o.CloseTime.ToString("HH:mm"),
                IsClosed = o.IsClosed
            }).ToList()
            : Enum.GetValues<DayOfWeek>().Select(d => new OperatingHourDto
            {
                DayOfWeek = d,
                DayName = d.ToString(),
                OpenTime = d == DayOfWeek.Friday ? "14:00" : "08:00",
                CloseTime = "23:00",
                IsClosed = false
            }).ToList();

        return new VenueResponse
        {
            Id = venue.Id,
            OwnerId = venue.OwnerId,
            Name = venue.Name,
            Description = venue.Description,
            City = venue.City,
            Area = venue.Area,
            Address = venue.Address,
            Country = venue.Country,
            Latitude = venue.Latitude,
            Longitude = venue.Longitude,
            Phone = venue.Phone,
            Email = venue.Email,
            Website = venue.Website,
            IsActive = venue.IsActive,
            IsVerified = venue.IsVerified,
            AverageRating = venue.AverageRating,
            TotalReviews = venue.TotalReviews,
            CreatedAt = venue.CreatedAt,
            Sports = sports,
            Courts = venue.Courts.Select(c => new CourtResponse
            {
                Id = c.Id,
                VenueId = c.VenueId,
                Name = c.Name,
                Description = c.Description,
                SportType = c.SportType.ToString(),
                PricePerHour = c.PricePerHour,
                SurfaceType = c.SurfaceType,
                IsIndoor = c.IsIndoor,
                Capacity = c.Capacity,
                IsActive = c.IsActive,
                Schedules = c.Schedules?.Select(s => new ScheduleResponse
                {
                    Id = s.Id,
                    CourtId = s.CourtId,
                    DayOfWeek = (int)s.DayOfWeek,
                    OpenTime = s.OpenTime.ToString("HH:mm"),
                    CloseTime = s.CloseTime.ToString("HH:mm")
                }).ToList() ?? new List<ScheduleResponse>()
            }).ToList(),
            Amenities = venue.Amenities?.Select(va => new VenueAmenityDto
            {
                Id = va.AmenityId,
                Name = va.Amenity?.Name ?? "",
                Icon = va.Amenity?.Icon ?? "bi-check-circle",
                Category = va.Amenity?.Category ?? "General"
            }).Where(a => !string.IsNullOrEmpty(a.Name)).ToList() ?? [],
            Images = images,
            OperatingHours = opHours,
            CancellationPolicy = venue.CancellationPolicy is not null ? new CancellationPolicyDto
            {
                FreeCancellationHours = venue.CancellationPolicy.FreeCancellationHours,
                LateCancellationFeePercent = venue.CancellationPolicy.LateCancellationFeePercent,
                PolicyDescription = venue.CancellationPolicy.PolicyDescription
            } : new CancellationPolicyDto
            {
                FreeCancellationHours = 24,
                LateCancellationFeePercent = 50.0m,
                PolicyDescription = "Free cancellation up to 24 hours before your booking."
            }
        };
    }
}

using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Infrastructure.Services;

public class FavoriteService : IFavoriteService
{
    private readonly AppDbContext _db;

    public FavoriteService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Result> AddFavoriteAsync(Guid userId, Guid venueId)
    {
        var venueExists = await _db.Venues.AnyAsync(v => v.Id == venueId && v.IsActive);
        if (!venueExists)
            return Error.NotFound("Venue");

        var alreadyFavorited = await _db.Favorites.AnyAsync(f => f.UserId == userId && f.VenueId == venueId);
        if (alreadyFavorited)
            return Result.Ok(); // Idempotent: already favorited

        var favorite = new Favorite
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            VenueId = venueId,
            CreatedAt = DateTime.UtcNow
        };

        _db.Favorites.Add(favorite);
        await _db.SaveChangesAsync();

        return Result.Ok();
    }

    public async Task<Result> RemoveFavoriteAsync(Guid userId, Guid venueId)
    {
        var favorite = await _db.Favorites.FirstOrDefaultAsync(f => f.UserId == userId && f.VenueId == venueId);
        if (favorite is null)
            return Result.Ok(); // Idempotent: already not favorited

        _db.Favorites.Remove(favorite);
        await _db.SaveChangesAsync();

        return Result.Ok();
    }

    public async Task<PagedResult<VenueCardDto>> GetMyFavoritesAsync(Guid userId, PagedRequest request)
    {
        var query = _db.Favorites
            .AsNoTracking()
            .Where(f => f.UserId == userId && f.Venue.IsActive)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => f.Venue);

        var totalCount = await query.CountAsync();

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

    public async Task<bool> IsFavoriteAsync(Guid userId, Guid venueId)
    {
        return await _db.Favorites.AnyAsync(f => f.UserId == userId && f.VenueId == venueId);
    }
}

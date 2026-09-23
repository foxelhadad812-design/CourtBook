using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Infrastructure.Services;

public class CourtAddonService : ICourtAddonService
{
    private readonly AppDbContext _dbContext;

    public CourtAddonService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<CourtAddonDto>> GetAddonsByCourtIdAsync(Guid courtId, bool onlyAvailable = true)
    {
        var query = _dbContext.CourtAddons.AsNoTracking().Where(a => a.CourtId == courtId);
        if (onlyAvailable)
        {
            query = query.Where(a => a.IsAvailable);
        }

        var addons = await query.ToListAsync();
        return addons.Select(MapToDto).ToList();
    }

    public async Task<CourtAddonDto> CreateCourtAddonAsync(Guid ownerId, CreateCourtAddonDto dto)
    {
        var court = await _dbContext.Courts
            .Include(c => c.Venue)
            .FirstOrDefaultAsync(c => c.Id == dto.CourtId && c.Venue.OwnerId == ownerId);

        if (court == null)
        {
            throw new UnauthorizedAccessException("You do not have permission to manage add-ons for this court.");
        }

        var addon = new CourtAddon
        {
            Id = Guid.NewGuid(),
            CourtId = dto.CourtId,
            Name = dto.Name.Trim(),
            NameAr = dto.NameAr?.Trim(),
            Price = dto.Price,
            Unit = string.IsNullOrWhiteSpace(dto.Unit) ? "Item" : dto.Unit.Trim(),
            IsAvailable = dto.IsAvailable
        };

        _dbContext.CourtAddons.Add(addon);
        await _dbContext.SaveChangesAsync();

        return MapToDto(addon);
    }

    public async Task<CourtAddonDto> UpdateCourtAddonAsync(Guid ownerId, Guid addonId, UpdateCourtAddonDto dto)
    {
        var addon = await _dbContext.CourtAddons
            .Include(a => a.Court)
            .ThenInclude(c => c.Venue)
            .FirstOrDefaultAsync(a => a.Id == addonId && a.Court.Venue.OwnerId == ownerId);

        if (addon == null)
        {
            throw new UnauthorizedAccessException("You do not have permission to modify this add-on.");
        }

        addon.Name = dto.Name.Trim();
        addon.NameAr = dto.NameAr?.Trim();
        addon.Price = dto.Price;
        addon.Unit = string.IsNullOrWhiteSpace(dto.Unit) ? "Item" : dto.Unit.Trim();
        addon.IsAvailable = dto.IsAvailable;

        await _dbContext.SaveChangesAsync();

        return MapToDto(addon);
    }

    public async Task<bool> DeleteCourtAddonAsync(Guid ownerId, Guid addonId)
    {
        var addon = await _dbContext.CourtAddons
            .Include(a => a.Court)
            .ThenInclude(c => c.Venue)
            .FirstOrDefaultAsync(a => a.Id == addonId && a.Court.Venue.OwnerId == ownerId);

        if (addon == null) return false;

        _dbContext.CourtAddons.Remove(addon);
        await _dbContext.SaveChangesAsync();
        return true;
    }

    private static CourtAddonDto MapToDto(CourtAddon a) => new()
    {
        Id = a.Id,
        CourtId = a.CourtId,
        Name = a.Name,
        NameAr = a.NameAr,
        Price = a.Price,
        Unit = a.Unit,
        IsAvailable = a.IsAvailable
    };
}

using CourtBook.Application.Common;
using CourtBook.Application.DTOs;

namespace CourtBook.Application.Interfaces;

public interface IFavoriteService
{
    Task<Result> AddFavoriteAsync(Guid userId, Guid venueId);
    Task<Result> RemoveFavoriteAsync(Guid userId, Guid venueId);
    Task<PagedResult<VenueCardDto>> GetMyFavoritesAsync(Guid userId, PagedRequest request);
    Task<bool> IsFavoriteAsync(Guid userId, Guid venueId);
}

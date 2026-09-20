using CourtBook.API.Extensions;
using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CourtBook.API.Controllers;

[ApiController]
[Route("api/favorites")]
[Authorize]
public class FavoritesController : ControllerBase
{
    private readonly IFavoriteService _favoriteService;

    public FavoritesController(IFavoriteService favoriteService)
    {
        _favoriteService = favoriteService;
    }

    /// <summary>
    /// Gets the current player's saved favorite venues with pagination.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<VenueCardDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyFavorites([FromQuery] PagedRequest request)
    {
        var userId = User.GetUserId();
        var result = await _favoriteService.GetMyFavoritesAsync(userId, request);
        return Ok(result);
    }

    /// <summary>
    /// Checks whether a venue is favorited by the current user.
    /// </summary>
    [HttpGet("{venueId}/status")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckFavorite(Guid venueId)
    {
        var userId = User.GetUserId();
        var isFav = await _favoriteService.IsFavoriteAsync(userId, venueId);
        return Ok(new { isFavorited = isFav });
    }

    /// <summary>
    /// Adds a venue to the user's favorites.
    /// </summary>
    [HttpPost("{venueId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddFavorite(Guid venueId)
    {
        var userId = User.GetUserId();
        var result = await _favoriteService.AddFavoriteAsync(userId, venueId);
        return result.ToActionResult();
    }

    /// <summary>
    /// Removes a venue from the user's favorites.
    /// </summary>
    [HttpDelete("{venueId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveFavorite(Guid venueId)
    {
        var userId = User.GetUserId();
        var result = await _favoriteService.RemoveFavoriteAsync(userId, venueId);
        return result.ToActionResult();
    }
}

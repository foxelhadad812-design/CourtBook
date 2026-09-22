using CourtBook.API.Extensions;
using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CourtBook.API.Controllers;

[ApiController]
[Route("api/community/players")]
[Authorize]
public class PlayerCommunityController : ControllerBase
{
    private readonly IPlayerCommunityService _playerCommunityService;

    public PlayerCommunityController(IPlayerCommunityService playerCommunityService)
    {
        _playerCommunityService = playerCommunityService;
    }

    /// <summary>
    /// Searches active community players with optional sport and city filtering.
    /// </summary>
    [HttpGet("search")]
    [ProducesResponseType(typeof(PagedResult<PublicPlayerProfileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchPlayers([FromQuery] SearchPlayersRequest request)
    {
        var callerId = User.GetUserId();
        var result = await _playerCommunityService.SearchPlayersAsync(callerId, request);
        return Ok(result);
    }

    /// <summary>
    /// Gets the public profile, sports skill levels, and transparent reputation metrics of a player.
    /// </summary>
    [HttpGet("{id}/profile")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PublicPlayerProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlayerProfile(Guid id)
    {
        Guid? callerId = User.Identity?.IsAuthenticated == true ? User.GetUserId() : null;
        if (callerId == Guid.Empty) callerId = null;

        var result = await _playerCommunityService.GetPublicPlayerProfileAsync(callerId, id);
        return result.ToActionResult();
    }

    /// <summary>
    /// Gets the explainable reputation and attendance reliability metrics for a player.
    /// </summary>
    [HttpGet("{id}/reputation")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PlayerReputationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlayerReputation(Guid id)
    {
        var result = await _playerCommunityService.GetPlayerReputationAsync(id);
        return result.ToActionResult();
    }

    /// <summary>
    /// Gets the past match and game history of a player with sport and date range filtering.
    /// </summary>
    [HttpGet("{id}/history")]
    [ProducesResponseType(typeof(PagedResult<GameHistoryItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGameHistory(Guid id, [FromQuery] GameHistoryFilterRequest request)
    {
        var callerId = User.GetUserId();
        var result = await _playerCommunityService.GetPlayerGameHistoryAsync(callerId, id, request);
        return result.ToActionResult();
    }
}

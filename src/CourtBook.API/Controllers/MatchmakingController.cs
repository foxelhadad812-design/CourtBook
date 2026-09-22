using CourtBook.API.Extensions;
using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CourtBook.API.Controllers;

[ApiController]
[Route("api/matchmaking")]
[Authorize]
public class MatchmakingController : ControllerBase
{
    private readonly IMatchmakingService _matchmakingService;

    public MatchmakingController(IMatchmakingService matchmakingService)
    {
        _matchmakingService = matchmakingService;
    }

    /// <summary>
    /// Gets deterministic, explainable matchmaking recommendations for the authenticated player.
    /// Evaluates sport preferences, sport-specific skill levels, timing, and geographic location.
    /// </summary>
    [HttpGet("recommendations")]
    [ProducesResponseType(typeof(List<MatchmakingRecommendationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecommendations([FromQuery] MatchmakingQueryRequest query)
    {
        var userId = User.GetUserId();
        var result = await _matchmakingService.GetRecommendationsAsync(userId, query);
        return result.ToActionResult();
    }

    /// <summary>
    /// Gets the authenticated player's matchmaking preferences.
    /// </summary>
    [HttpGet("preferences")]
    [ProducesResponseType(typeof(PlayerPreferenceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPreferences()
    {
        var userId = User.GetUserId();
        var result = await _matchmakingService.GetPlayerPreferenceAsync(userId);
        return result.ToActionResult();
    }

    /// <summary>
    /// Updates the authenticated player's matchmaking preferences.
    /// </summary>
    [HttpPut("preferences")]
    [ProducesResponseType(typeof(PlayerPreferenceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdatePlayerPreferenceRequest request)
    {
        var userId = User.GetUserId();
        var result = await _matchmakingService.UpdatePlayerPreferenceAsync(userId, request);
        return result.ToActionResult();
    }

    /// <summary>
    /// Gets all sport-specific skill ratings registered for the authenticated player.
    /// </summary>
    [HttpGet("skills")]
    [ProducesResponseType(typeof(List<PlayerSportSkillDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSkills()
    {
        var userId = User.GetUserId();
        var result = await _matchmakingService.GetPlayerSkillsAsync(userId);
        return result.ToActionResult();
    }

    /// <summary>
    /// Upserts a sport-specific skill rating for the authenticated player.
    /// </summary>
    [HttpPost("skills")]
    [ProducesResponseType(typeof(PlayerSportSkillDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpsertSkill([FromBody] UpsertPlayerSportSkillRequest request)
    {
        var userId = User.GetUserId();
        var result = await _matchmakingService.UpsertPlayerSkillAsync(userId, request);
        return result.ToActionResult();
    }
}

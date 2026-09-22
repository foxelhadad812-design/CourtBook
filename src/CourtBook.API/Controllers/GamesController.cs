using CourtBook.API.Extensions;
using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CourtBook.API.Controllers;

[ApiController]
[Route("api/games")]
public class GamesController : ControllerBase
{
    private readonly IGameService _gameService;

    public GamesController(IGameService gameService)
    {
        _gameService = gameService;
    }

    /// <summary>
    /// Searches and filters open community games and public matches with pagination.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResult<GameResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchGames([FromQuery] GameSearchRequest request)
    {
        Guid? currentUserId = User.Identity?.IsAuthenticated == true ? User.GetUserId() : null;
        if (currentUserId == Guid.Empty) currentUserId = null;
        var result = await _gameService.SearchGamesAsync(request, currentUserId);
        return Ok(result);
    }

    /// <summary>
    /// Gets details of a specific game including participants.
    /// </summary>
    [HttpGet("{id}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(GameResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        Guid? currentUserId = User.Identity?.IsAuthenticated == true ? User.GetUserId() : null;
        if (currentUserId == Guid.Empty) currentUserId = null;
        var result = await _gameService.GetByIdAsync(id, currentUserId);
        return result.ToActionResult();
    }

    /// <summary>
    /// Creates a new public match or game.
    /// </summary>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(GameResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateGame([FromBody] CreateGameRequest request)
    {
        var creatorId = User.GetUserId();
        var result = await _gameService.CreateGameAsync(creatorId, request);
        return result.ToCreatedResult($"/api/games/{result.Value?.Id}");
    }

    /// <summary>
    /// Joins an open game. Enforces max player capacity and prevents duplicate participation.
    /// Supports access code for private matches.
    /// </summary>
    [HttpPost("{id}/join")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> JoinGame(Guid id, [FromBody] JoinGameRequest? request = null)
    {
        var userId = User.GetUserId();
        var result = await _gameService.JoinGameAsync(userId, id, request?.AccessCode);
        return result.ToActionResult();
    }

    /// <summary>
    /// Leaves a joined game.
    /// </summary>
    [HttpPost("{id}/leave")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LeaveGame(Guid id)
    {
        var userId = User.GetUserId();
        var result = await _gameService.LeaveGameAsync(userId, id);
        return result.ToActionResult();
    }

    /// <summary>
    /// Cancels a game (Creator or Admin only).
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelGame(Guid id)
    {
        var userId = User.GetUserId();
        var userRole = User.GetUserRole();
        var result = await _gameService.CancelGameAsync(userId, userRole, id);
        return result.ToActionResult();
    }

    /// <summary>
    /// Gets the game lobby including players, ready states, and team allocations.
    /// </summary>
    [HttpGet("{id}/lobby")]
    [Authorize]
    [ProducesResponseType(typeof(GameResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLobby(Guid id)
    {
        var userId = User.GetUserId();
        var result = await _gameService.GetGameLobbyAsync(userId, id);
        return result.ToActionResult();
    }

    /// <summary>
    /// Sets or updates the ready status of the authenticated player in the game lobby.
    /// </summary>
    [HttpPut("{id}/ready")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetReady(Guid id, [FromBody] SetPlayerReadyRequest request)
    {
        var userId = User.GetUserId();
        var result = await _gameService.SetPlayerReadyAsync(userId, id, request.IsReady);
        return result.ToActionResult();
    }

    /// <summary>
    /// Assigns a participant to a specific team (organizer only).
    /// </summary>
    [HttpPut("{id}/teams")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AssignTeam(Guid id, [FromBody] AssignTeamRequest request)
    {
        var organizerId = User.GetUserId();
        var result = await _gameService.AssignTeamAsync(organizerId, id, request.ParticipantUserId, request.Team);
        return result.ToActionResult();
    }

    /// <summary>
    /// Deterministically balances teams using snake-draft algorithm based on player sport skills (organizer only).
    /// </summary>
    [HttpPost("{id}/balance-teams")]
    [Authorize]
    [ProducesResponseType(typeof(BalanceTeamsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> BalanceTeams(Guid id)
    {
        var organizerId = User.GetUserId();
        var result = await _gameService.BalanceTeamsAsync(organizerId, id);
        return result.ToActionResult();
    }
}

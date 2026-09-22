using CourtBook.API.Extensions;
using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CourtBook.API.Controllers;

[ApiController]
[Route("api/invitations")]
[Authorize]
[EnableRateLimiting("community")]
public class InvitationsController : ControllerBase
{
    private readonly IInvitationService _invitationService;

    public InvitationsController(IInvitationService invitationService)
    {
        _invitationService = invitationService;
    }

    /// <summary>
    /// Invites a player to join a specific match.
    /// </summary>
    [HttpPost("~/api/games/{gameId}/invitations")]
    [ProducesResponseType(typeof(GameInvitationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateInvitation(Guid gameId, [FromBody] CreateInvitationRequest request)
    {
        var inviterId = User.GetUserId();
        var result = await _invitationService.CreateInvitationAsync(inviterId, gameId, request);
        return result.ToCreatedResult($"/api/invitations/{result.Value?.Id}");
    }

    /// <summary>
    /// Gets received game invitations for the authenticated player.
    /// </summary>
    [HttpGet("received")]
    [ProducesResponseType(typeof(PagedResult<GameInvitationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReceivedInvitations([FromQuery] PagedRequest request)
    {
        var userId = User.GetUserId();
        var result = await _invitationService.GetReceivedInvitationsAsync(userId, request);
        return Ok(result);
    }

    /// <summary>
    /// Gets sent game invitations from the authenticated player.
    /// </summary>
    [HttpGet("sent")]
    [ProducesResponseType(typeof(PagedResult<GameInvitationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSentInvitations([FromQuery] PagedRequest request)
    {
        var userId = User.GetUserId();
        var result = await _invitationService.GetSentInvitationsAsync(userId, request);
        return Ok(result);
    }

    /// <summary>
    /// Accepts a game invitation and automatically joins the match using concurrency-protected game-join pipeline.
    /// </summary>
    [HttpPost("{id}/accept")]
    [ProducesResponseType(typeof(GameResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AcceptInvitation(Guid id)
    {
        var userId = User.GetUserId();
        var result = await _invitationService.AcceptInvitationAsync(userId, id);
        return result.ToActionResult();
    }

    /// <summary>
    /// Declines a game invitation.
    /// </summary>
    [HttpPost("{id}/decline")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeclineInvitation(Guid id)
    {
        var userId = User.GetUserId();
        var result = await _invitationService.DeclineInvitationAsync(userId, id);
        return result.ToActionResult();
    }

    /// <summary>
    /// Cancels a sent invitation (inviter or game creator only).
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelInvitation(Guid id)
    {
        var userId = User.GetUserId();
        var result = await _invitationService.CancelInvitationAsync(userId, id);
        return result.ToActionResult();
    }
}

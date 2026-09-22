using CourtBook.API.Extensions;
using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CourtBook.API.Controllers;

[ApiController]
[Route("api/connections")]
[Authorize]
[EnableRateLimiting("community")]
public class ConnectionsController : ControllerBase
{
    private readonly IConnectionService _connectionService;

    public ConnectionsController(IConnectionService connectionService)
    {
        _connectionService = connectionService;
    }

    /// <summary>
    /// Gets confirmed connections for the authenticated player.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<PlayerConnectionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConnections([FromQuery] ConnectionStatus? status, [FromQuery] PagedRequest request)
    {
        var userId = User.GetUserId();
        var result = await _connectionService.GetConnectionsAsync(userId, status, request);
        return Ok(result);
    }

    /// <summary>
    /// Sends a connection request to another player.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(PlayerConnectionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SendConnectionRequest([FromBody] SendConnectionRequest request)
    {
        var requesterId = User.GetUserId();
        var result = await _connectionService.SendConnectionRequestAsync(requesterId, request.TargetUserId);
        return result.ToCreatedResult($"/api/connections/{result.Value?.Id}");
    }

    /// <summary>
    /// Accepts a pending connection request.
    /// </summary>
    [HttpPost("{id}/accept")]
    [ProducesResponseType(typeof(PlayerConnectionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AcceptConnectionRequest(Guid id)
    {
        var userId = User.GetUserId();
        var result = await _connectionService.AcceptConnectionRequestAsync(userId, id);
        return result.ToActionResult();
    }

    /// <summary>
    /// Declines a pending connection request.
    /// </summary>
    [HttpPost("{id}/decline")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeclineConnectionRequest(Guid id)
    {
        var userId = User.GetUserId();
        var result = await _connectionService.DeclineConnectionRequestAsync(userId, id);
        return result.ToActionResult();
    }

    /// <summary>
    /// Removes or cancels a connection relationship.
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveConnection(Guid id)
    {
        var userId = User.GetUserId();
        var result = await _connectionService.CancelOrRemoveConnectionAsync(userId, id);
        return result.ToActionResult();
    }

    /// <summary>
    /// Blocks a target user from sending invitations or connection requests.
    /// </summary>
    [HttpPost("block")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BlockUser([FromBody] BlockUserRequest request)
    {
        var callerId = User.GetUserId();
        var result = await _connectionService.BlockUserAsync(callerId, request.TargetUserId);
        return result.ToActionResult();
    }

    /// <summary>
    /// Unblocks a previously blocked user.
    /// </summary>
    [HttpPost("unblock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnblockUser([FromBody] BlockUserRequest request)
    {
        var callerId = User.GetUserId();
        var result = await _connectionService.UnblockUserAsync(callerId, request.TargetUserId);
        return result.ToActionResult();
    }

    /// <summary>
    /// Gets all users blocked by the authenticated player.
    /// </summary>
    [HttpGet("blocked")]
    [ProducesResponseType(typeof(PagedResult<PlayerConnectionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBlockedUsers([FromQuery] PagedRequest request)
    {
        var userId = User.GetUserId();
        var result = await _connectionService.GetBlockedUsersAsync(userId, request);
        return Ok(result);
    }
}

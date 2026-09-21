using CourtBook.API.Extensions;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CourtBook.API.Controllers;

[ApiController]
[Route("api/admin/venues")]
[Authorize(Roles = "Admin")]
public class AdminVenuesController : ControllerBase
{
    private readonly IAdminVenueService _adminVenueService;

    public AdminVenuesController(IAdminVenueService adminVenueService)
    {
        _adminVenueService = adminVenueService;
    }

    /// <summary>
    /// Returns all venues across the platform for admin review, optionally filtered by approval status.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<AdminVenueDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] VenueApprovalStatus? status)
    {
        var result = await _adminVenueService.GetAllVenuesAsync(status);
        return Ok(result);
    }

    /// <summary>
    /// Approves a pending or reviewed facility, making it eligible for active marketplace listing.
    /// </summary>
    [HttpPost("{id}/approve")]
    [ProducesResponseType(typeof(AdminVenueDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Approve(Guid id)
    {
        var adminId = User.GetUserId();
        var venue = await _adminVenueService.ApproveVenueAsync(adminId, id);
        return venue is null ? NotFound("Facility not found.") : Ok(venue);
    }

    /// <summary>
    /// Rejects a facility with an explicit reason provided by the administrator.
    /// </summary>
    [HttpPost("{id}/reject")]
    [ProducesResponseType(typeof(AdminVenueDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectVenueRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Reason))
            return BadRequest("Rejection reason is required.");

        var adminId = User.GetUserId();
        var venue = await _adminVenueService.RejectVenueAsync(adminId, id, request);
        return venue is null ? NotFound("Facility not found.") : Ok(venue);
    }
}

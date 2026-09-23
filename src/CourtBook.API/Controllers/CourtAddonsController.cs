using CourtBook.API.Extensions;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CourtBook.API.Controllers;

[ApiController]
[Route("api/courts/{courtId}/addons")]
public class CourtAddonsController : ControllerBase
{
    private readonly ICourtAddonService _addonService;

    public CourtAddonsController(ICourtAddonService addonService)
    {
        _addonService = addonService;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAddons(Guid courtId, [FromQuery] bool onlyAvailable = true)
    {
        var addons = await _addonService.GetAddonsByCourtIdAsync(courtId, onlyAvailable);
        return Ok(addons);
    }

    [HttpPost]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<IActionResult> CreateAddon(Guid courtId, [FromBody] CreateCourtAddonDto dto)
    {
        dto.CourtId = courtId;
        try
        {
            var created = await _addonService.CreateCourtAddonAsync(User.GetUserId(), dto);
            return CreatedAtAction(nameof(GetAddons), new { courtId }, created);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<IActionResult> UpdateAddon(Guid courtId, Guid id, [FromBody] UpdateCourtAddonDto dto)
    {
        try
        {
            var updated = await _addonService.UpdateCourtAddonAsync(User.GetUserId(), id, dto);
            return Ok(updated);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<IActionResult> DeleteAddon(Guid courtId, Guid id)
    {
        var success = await _addonService.DeleteCourtAddonAsync(User.GetUserId(), id);
        return success ? NoContent() : NotFound(new { error = "Add-on not found or unauthorized." });
    }
}

using CourtBook.API.Extensions;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CourtBook.API.Controllers;

[ApiController]
[Route("api/profile")]
[Authorize] // Requires authentication
public class ProfileController : ControllerBase
{
    private readonly IProfileService _profileService;

    public ProfileController(IProfileService profileService)
    {
        _profileService = profileService;
    }

    [HttpGet]
    public async Task<IActionResult> GetProfile()
    {
        var profile = await _profileService.GetProfileAsync(User.GetUserId());
        if (profile is null) return NotFound("User not found.");
        return Ok(profile);
    }

    [HttpPut]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        try
        {
            var profile = await _profileService.UpdateProfileAsync(User.GetUserId(), request);
            if (profile is null) return NotFound("User not found.");
            return Ok(profile);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferences()
    {
        var prefs = await _profileService.GetPreferencesAsync(User.GetUserId());
        if (prefs is null) return NotFound("Preferences not found.");
        return Ok(prefs);
    }

    [HttpPut("preferences")]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdatePreferencesRequest request)
    {
        var prefs = await _profileService.UpdatePreferencesAsync(User.GetUserId(), request);
        if (prefs is null) return NotFound("User not found.");
        return Ok(prefs);
    }
}

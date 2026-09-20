using CourtBook.API.Extensions;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CourtBook.API.Controllers;

[ApiController]
[Route("api/courts/{courtId}/availability")]
public class AvailabilityController : ControllerBase
{
    private readonly IAvailabilityService _availabilityService;

    public AvailabilityController(IAvailabilityService availabilityService)
    {
        _availabilityService = availabilityService;
    }

    /// <summary>
    /// Computes real availability slots for a specific court on a given date.
    /// Incorporates court schedules, existing bookings, past-time cutoff, and dynamic price rules.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CourtAvailabilityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAvailability(Guid courtId, [FromQuery] DateOnly? date, [FromQuery] int durationMinutes = 60)
    {
        var targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var request = new CourtAvailabilityRequest
        {
            Date = targetDate,
            DurationMinutes = durationMinutes
        };

        var result = await _availabilityService.GetCourtAvailabilityAsync(courtId, request);
        return result.ToActionResult();
    }
}

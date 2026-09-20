using CourtBook.API.Extensions;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CourtBook.API.Controllers;

[ApiController]
[Route("api/venues/{venueId}/courts/{courtId}/schedules")]
public class CourtSchedulesController : ControllerBase
{
    private readonly ICourtService _courtService;

    public CourtSchedulesController(ICourtService courtService)
    {
        _courtService = courtService;
    }

    [HttpPost]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> Create(Guid venueId, Guid courtId, [FromBody] CreateScheduleRequest request)
    {
        try
        {
            var schedule = await _courtService.AddScheduleAsync(User.GetUserId(), venueId, courtId, request);
            return schedule is null ? NotFound("Court not found.") : Ok(schedule);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> Delete(Guid venueId, Guid courtId, Guid id)
    {
        try
        {
            var success = await _courtService.DeleteScheduleAsync(User.GetUserId(), venueId, courtId, id);
            return success ? NoContent() : NotFound();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
    }
}

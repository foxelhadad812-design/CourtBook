using CourtBook.API.Extensions;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CourtBook.API.Controllers;

[ApiController]
[Route("api/venues/{venueId}/courts")]
public class CourtsController : ControllerBase
{
    private readonly ICourtService _courtService;

    public CourtsController(ICourtService courtService)
    {
        _courtService = courtService;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll(Guid venueId)
    {
        return Ok(await _courtService.GetAllByVenueAsync(venueId));
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetById(Guid venueId, Guid id)
    {
        var court = await _courtService.GetByIdAsync(venueId, id);
        return court is null ? NotFound() : Ok(court);
    }

    [HttpGet("/api/courts/{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetDirectById(Guid id)
    {
        var court = await _courtService.GetByIdAsync(id);
        return court is null ? NotFound() : Ok(court);
    }

    [HttpPost]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> Create(Guid venueId, [FromBody] CreateCourtRequest request)
    {
        try
        {
            var court = await _courtService.CreateAsync(User.GetUserId(), venueId, request);
            return court is null ? NotFound("Venue not found.") : CreatedAtAction(nameof(GetById), new { venueId, id = court.Id }, court);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> Update(Guid venueId, Guid id, [FromBody] UpdateCourtRequest request)
    {
        try
        {
            var court = await _courtService.UpdateAsync(User.GetUserId(), venueId, id, request);
            return court is null ? NotFound() : Ok(court);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<IActionResult> Delete(Guid venueId, Guid id)
    {
        try
        {
            var success = await _courtService.DeleteAsync(User.GetUserId(), User.GetUserRole(), venueId, id);
            return success ? NoContent() : NotFound();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
    }
}

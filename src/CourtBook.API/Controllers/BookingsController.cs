using CourtBook.API.Extensions;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CourtBook.API.Controllers;

[ApiController]
[Route("api/bookings")]
[Authorize] // All endpoints require auth
public class BookingsController : ControllerBase
{
    private readonly IBookingService _bookingService;

    public BookingsController(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    [HttpPost]
    [Authorize(Roles = "Client,Owner,Admin")]
    public async Task<IActionResult> Create([FromBody] CreateBookingRequest request)
    {
        try
        {
            var booking = await _bookingService.CreateAsync(User.GetUserId(), request);
            return CreatedAtAction(nameof(GetById), new { id = booking.Id }, booking);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpGet("my")]
    public async Task<IActionResult> GetMyBookings([FromQuery] BookingQueryRequest request)
    {
        var result = await _bookingService.GetMyBookingsPagedAsync(User.GetUserId(), request);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            var booking = await _bookingService.GetByIdAsync(User.GetUserId(), User.GetUserRole(), id);
            return booking is null ? NotFound() : Ok(booking);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    [HttpGet("{id}/cancellation-preview")]
    public async Task<IActionResult> GetCancellationPreview(Guid id)
    {
        try
        {
            var preview = await _bookingService.GetCancellationPreviewAsync(User.GetUserId(), User.GetUserRole(), id);
            return Ok(preview);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    [HttpPost("{id}/cancel")]
    [HttpPut("{id}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelBookingRequest? request = null)
    {
        try
        {
            var result = await _bookingService.CancelWithPolicyAsync(User.GetUserId(), User.GetUserRole(), id, request);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

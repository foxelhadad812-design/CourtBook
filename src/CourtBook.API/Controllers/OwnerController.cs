using CourtBook.API.Extensions;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CourtBook.API.Controllers;

[ApiController]
[Route("api/owner")]
[Authorize(Roles = "Owner,Admin")] // Protected: Owner and Admin only
public class OwnerController : ControllerBase
{
    private readonly IOwnerService _ownerService;

    public OwnerController(IOwnerService ownerService)
    {
        _ownerService = ownerService;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboardSummary()
    {
        var summary = await _ownerService.GetDashboardSummaryAsync(User.GetUserId());
        return Ok(summary);
    }

    [HttpGet("venues")]
    public async Task<IActionResult> GetOwnerVenues()
    {
        var venues = await _ownerService.GetOwnerVenuesAsync(User.GetUserId());
        return Ok(venues);
    }

    [HttpGet("venues/{id}")]
    public async Task<IActionResult> GetOwnerVenueById(Guid id)
    {
        try
        {
            var venue = await _ownerService.GetOwnerVenueByIdAsync(User.GetUserId(), id);
            return venue is null ? NotFound(new { error = "Facility not found." }) : Ok(venue);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    [HttpGet("bookings")]
    public async Task<IActionResult> GetOwnerBookings([FromQuery] OwnerBookingQueryRequest request)
    {
        var bookings = await _ownerService.GetOwnerBookingsAsync(User.GetUserId(), request);
        return Ok(bookings);
    }

    [HttpGet("bookings/{id}")]
    public async Task<IActionResult> GetOwnerBookingById(Guid id)
    {
        try
        {
            var booking = await _ownerService.GetOwnerBookingByIdAsync(User.GetUserId(), id);
            return booking is null ? NotFound(new { error = "Booking not found." }) : Ok(booking);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    [HttpGet("analytics")]
    public async Task<IActionResult> GetOwnerAnalytics()
    {
        var analytics = await _ownerService.GetOwnerAnalyticsAsync(User.GetUserId());
        return Ok(analytics);
    }
}

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

    // ── Phase 4B: Venue Management Endpoints ────────────────────────────────

    [HttpGet("venues/{id}/details")]
    public async Task<IActionResult> GetOwnerVenueDetails(Guid id)
    {
        try
        {
            var venue = await _ownerService.GetOwnerVenueDetailsAsync(User.GetUserId(), id);
            return venue is null ? NotFound(new { error = "Facility not found." }) : Ok(venue);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    [HttpPost("venues")]
    public async Task<IActionResult> CreateVenue([FromBody] CreateVenueRequest request)
    {
        try
        {
            var venue = await _ownerService.CreateVenueAsync(User.GetUserId(), request);
            return CreatedAtAction(nameof(GetOwnerVenueById), new { id = venue.Id }, venue);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPut("venues/{id}")]
    public async Task<IActionResult> UpdateVenue(Guid id, [FromBody] UpdateVenueRequest request)
    {
        try
        {
            var venue = await _ownerService.UpdateVenueAsync(User.GetUserId(), id, request);
            return Ok(venue);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPost("venues/{id}/deactivate")]
    public async Task<IActionResult> DeactivateVenue(Guid id)
    {
        try
        {
            var result = await _ownerService.DeactivateVenueAsync(User.GetUserId(), id);
            if (!result.Success)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    // ── Phase 4B: Court Management Endpoints ────────────────────────────────

    [HttpGet("venues/{venueId}/courts")]
    public async Task<IActionResult> GetVenueCourts(Guid venueId)
    {
        try
        {
            var courts = await _ownerService.GetOwnerVenueCourtsAsync(User.GetUserId(), venueId);
            return Ok(courts);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    [HttpGet("courts/{courtId}")]
    public async Task<IActionResult> GetCourtById(Guid courtId)
    {
        try
        {
            var court = await _ownerService.GetOwnerCourtByIdAsync(User.GetUserId(), courtId);
            return court is null ? NotFound(new { error = "Court not found." }) : Ok(court);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    [HttpPost("venues/{venueId}/courts")]
    public async Task<IActionResult> CreateCourt(Guid venueId, [FromBody] CreateCourtRequest request)
    {
        try
        {
            var court = await _ownerService.CreateCourtAsync(User.GetUserId(), venueId, request);
            return CreatedAtAction(nameof(GetCourtById), new { courtId = court.Id }, court);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPut("courts/{courtId}")]
    public async Task<IActionResult> UpdateCourt(Guid courtId, [FromBody] UpdateCourtRequest request)
    {
        try
        {
            var court = await _ownerService.UpdateCourtAsync(User.GetUserId(), courtId, request);
            return Ok(court);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPost("courts/{courtId}/deactivate")]
    public async Task<IActionResult> DeactivateCourt(Guid courtId)
    {
        try
        {
            var result = await _ownerService.DeactivateCourtAsync(User.GetUserId(), courtId);
            if (!result.Success)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    // ── Phase 4B: Amenities Management Endpoints ────────────────────────────

    [HttpGet("amenities")]
    public async Task<IActionResult> GetAmenitiesCatalog()
    {
        var catalog = await _ownerService.GetAmenitiesCatalogAsync();
        return Ok(catalog);
    }

    [HttpPut("venues/{venueId}/amenities")]
    public async Task<IActionResult> UpdateVenueAmenities(Guid venueId, [FromBody] List<Guid> amenityIds)
    {
        try
        {
            var updated = await _ownerService.UpdateVenueAmenitiesAsync(User.GetUserId(), venueId, amenityIds);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    // ── Phase 4B: Image Management Endpoints ────────────────────────────────

    [HttpPost("venues/{venueId}/images")]
    public async Task<IActionResult> AddVenueImage(Guid venueId, [FromBody] AddVenueImageRequest request)
    {
        try
        {
            var image = await _ownerService.AddVenueImageAsync(User.GetUserId(), venueId, request);
            return Ok(image);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpDelete("venues/{venueId}/images/{imageId}")]
    public async Task<IActionResult> DeleteVenueImage(Guid venueId, Guid imageId)
    {
        try
        {
            var deleted = await _ownerService.DeleteVenueImageAsync(User.GetUserId(), venueId, imageId);
            return deleted ? NoContent() : NotFound();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    [HttpPut("venues/{venueId}/images/{imageId}/primary")]
    public async Task<IActionResult> SetPrimaryVenueImage(Guid venueId, Guid imageId)
    {
        try
        {
            var updated = await _ownerService.SetPrimaryVenueImageAsync(User.GetUserId(), venueId, imageId);
            return updated ? Ok(new { success = true }) : NotFound();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    // ── Phase 4B: Operating Hours Management Endpoints ──────────────────────

    [HttpGet("venues/{venueId}/operating-hours")]
    public async Task<IActionResult> GetVenueOperatingHours(Guid venueId)
    {
        try
        {
            var hours = await _ownerService.GetVenueOperatingHoursAsync(User.GetUserId(), venueId);
            return Ok(hours);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    [HttpPut("venues/{venueId}/operating-hours")]
    public async Task<IActionResult> UpdateVenueOperatingHours(Guid venueId, [FromBody] List<UpdateOperatingHourRequest> hours)
    {
        try
        {
            var updated = await _ownerService.UpdateVenueOperatingHoursAsync(User.GetUserId(), venueId, hours);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    // ── Phase 12: Manual Booking & Quick Check-in Endpoints ──────────────────

    [HttpPost("manual-booking")]
    public async Task<IActionResult> CreateManualBooking([FromBody] CreateManualBookingRequest request)
    {
        try
        {
            var booking = await _ownerService.CreateManualBookingAsync(User.GetUserId(), request);
            return CreatedAtAction("GetOwnerBookingById", new { id = booking.Id }, booking);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    [HttpPost("check-in")]
    public async Task<IActionResult> QuickCheckIn([FromBody] QuickCheckInRequest request)
    {
        var result = await _ownerService.QuickCheckInAsync(User.GetUserId(), request);
        if (!result.Success)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }
}


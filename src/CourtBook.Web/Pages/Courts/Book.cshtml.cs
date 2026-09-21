using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Courts;

public class BookModel : PageModel
{
    private readonly ApiClient _api;

    public BookModel(ApiClient api)
    {
        _api = api;
    }

    public CourtResponse? Court { get; set; }
    public VenueResponse? Venue { get; set; }
    public CourtAvailabilityResponse? InitialAvailability { get; set; }
    public bool IsNotFound { get; set; } = false;
    public bool IsAuthenticated { get; set; } = false;
    public string? CurrentUserEmail { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Date { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");

    [BindProperty(SupportsGet = true)]
    public int DurationMinutes { get; set; } = 60;

    public async Task<IActionResult> OnGetAsync(Guid? id, [FromQuery] Guid? courtId, [FromQuery] Guid? venueId)
    {
        var targetCourtId = courtId ?? id;
        if (!targetCourtId.HasValue || targetCourtId.Value == Guid.Empty)
        {
            if (Request.Query.TryGetValue("courtId", out var qCourt) && Guid.TryParse(qCourt, out var parsedCourt))
            {
                targetCourtId = parsedCourt;
            }
            else if (Request.Query.TryGetValue("id", out var qId) && Guid.TryParse(qId, out var parsedId))
            {
                targetCourtId = parsedId;
            }
            else
            {
                IsNotFound = true;
                return Page();
            }
        }

        // Check authentication state
        var token = HttpContext.Session.GetString("JwtToken");
        IsAuthenticated = !string.IsNullOrEmpty(token);
        CurrentUserEmail = HttpContext.Session.GetString("UserName");

        try
        {
            // 1. Fetch Court details
            var courtResp = await _api.Client.GetAsync($"/api/courts/{targetCourtId.Value}");
            if (!courtResp.IsSuccessStatusCode)
            {
                IsNotFound = true;
                return Page();
            }
            Court = await courtResp.Content.ReadFromJsonAsync<CourtResponse>();
            if (Court == null)
            {
                IsNotFound = true;
                return Page();
            }

            // 2. Fetch Venue details
            var targetVenueId = venueId ?? Court.VenueId;
            var venueResp = await _api.Client.GetAsync($"/api/venues/{targetVenueId}");
            if (venueResp.IsSuccessStatusCode)
            {
                Venue = await venueResp.Content.ReadFromJsonAsync<VenueResponse>();
            }

            // 3. Parse date (default to today)
            if (!DateOnly.TryParse(Date, out var selectedDate) || selectedDate < DateOnly.FromDateTime(DateTime.UtcNow))
            {
                selectedDate = DateOnly.FromDateTime(DateTime.UtcNow);
                Date = selectedDate.ToString("yyyy-MM-dd");
            }

            // 4. Fetch initial real availability
            var availResp = await _api.Client.GetAsync($"/api/courts/{targetCourtId.Value}/availability?date={Date}&durationMinutes={DurationMinutes}");
            if (availResp.IsSuccessStatusCode)
            {
                InitialAvailability = await availResp.Content.ReadFromJsonAsync<CourtAvailabilityResponse>();
            }

            return Page();
        }
        catch
        {
            IsNotFound = true;
            return Page();
        }
    }

    /// <summary>
    /// Proxy handler for AJAX availability requests from frontend
    /// </summary>
    public async Task<IActionResult> OnGetAvailabilityAsync(Guid courtId, string date, int durationMinutes)
    {
        if (courtId == Guid.Empty) return BadRequest("CourtId is required.");

        if (!DateOnly.TryParse(date, out var parsedDate))
            parsedDate = DateOnly.FromDateTime(DateTime.UtcNow);

        if (durationMinutes < 30 || durationMinutes > 720)
            durationMinutes = 60;

        try
        {
            var response = await _api.Client.GetAsync($"/api/courts/{courtId}/availability?date={parsedDate:yyyy-MM-dd}&durationMinutes={durationMinutes}");
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<CourtAvailabilityResponse>();
                return new JsonResult(data);
            }

            var err = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, err);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    /// <summary>
    /// Handles AJAX booking creation with concurrency/race condition detection
    /// </summary>
    public async Task<IActionResult> OnPostCreateBookingAsync([FromBody] CreateBookingRequest request)
    {
        var token = HttpContext.Session.GetString("JwtToken");
        if (string.IsNullOrEmpty(token))
        {
            return StatusCode(401, new { success = false, message = "You must be signed in to create a reservation." });
        }

        if (request.StartTime >= request.EndTime)
        {
            return BadRequest(new { success = false, message = "StartTime must be before EndTime." });
        }

        try
        {
            var response = await _api.Client.PostAsJsonAsync("/api/bookings", request);

            if (response.IsSuccessStatusCode)
            {
                var booking = await response.Content.ReadFromJsonAsync<BookingResponse>();
                if (booking != null)
                {
                    // Store details in TempData for confirmation page
                    TempData["BookingId"] = booking.Id.ToString();
                    TempData["BookingReference"] = booking.BookingReference;
                    TempData["CourtName"] = booking.CourtName;
                    TempData["SportType"] = booking.SportType;
                    TempData["VenueName"] = booking.VenueName;
                    TempData["VenueCity"] = booking.VenueCity;
                    TempData["StartTime"] = booking.StartTime.ToString("o");
                    TempData["EndTime"] = booking.EndTime.ToString("o");
                    TempData["TotalPrice"] = booking.TotalPrice.ToString("0.00");
                    TempData["PaymentStatus"] = booking.PaymentStatus;

                    return new JsonResult(new { 
                        success = true, 
                        bookingId = booking.Id, 
                        bookingReference = booking.BookingReference,
                        redirectUrl = "/Bookings/Confirmation" 
                    });
                }
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // Concurrency Conflict: slot was booked by someone else in the meantime!
                return StatusCode(409, new { 
                    success = false, 
                    isConflict = true, 
                    message = "This time slot was just booked by another player. Please select another available slot." 
                });
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return StatusCode(401, new { success = false, message = "Your session has expired. Please sign in again." });
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return StatusCode(403, new { success = false, message = "You do not have permission to book this court." });
            }

            var errorBody = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, new { 
                success = false, 
                message = !string.IsNullOrWhiteSpace(errorBody) ? errorBody : "Unable to complete booking. Please try again." 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = $"Network or server error: {ex.Message}" });
        }
    }
}

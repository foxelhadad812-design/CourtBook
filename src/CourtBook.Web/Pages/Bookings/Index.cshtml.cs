using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Bookings;

public class IndexModel : PageModel
{
    private readonly ApiClient _api;

    public IndexModel(ApiClient api)
    {
        _api = api;
    }

    public PagedResult<BookingResponse> PagedBookings { get; set; } = PagedResult<BookingResponse>.Empty();
    
    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = "upcoming";

    [BindProperty(SupportsGet = true)]
    public string? Sport { get; set; }

    [BindProperty(SupportsGet = true)]
    public int P { get; set; } = 1;

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        try
        {
            var query = $"/api/bookings/my?status={Status}&sport={Sport ?? ""}&page={P}&pageSize=9";
            var resp = await _api.Client.GetAsync(query);

            if (resp.IsSuccessStatusCode)
            {
                var result = await resp.Content.ReadFromJsonAsync<PagedResult<BookingResponse>>();
                if (result != null)
                {
                    PagedBookings = result;
                }
            }
            else if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return RedirectToPage("/Login", new { returnUrl = "/Bookings" });
            }
            else
            {
                ErrorMessage = "Could not load bookings at this time. Please try again.";
            }
        }
        catch (Exception)
        {
            ErrorMessage = "Network error connecting to bookings service.";
        }

        return Page();
    }

    public async Task<IActionResult> OnGetCancellationPreviewAsync(Guid id)
    {
        try
        {
            var resp = await _api.Client.GetAsync($"/api/bookings/{id}/cancellation-preview");
            if (resp.IsSuccessStatusCode)
            {
                var preview = await resp.Content.ReadFromJsonAsync<CancellationPreviewResponse>();
                return new JsonResult(preview);
            }

            var err = await resp.Content.ReadAsStringAsync();
            return BadRequest(new { error = "Unable to fetch cancellation policy preview." });
        }
        catch
        {
            return StatusCode(500, new { error = "Failed to connect to cancellation service." });
        }
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid id, [FromForm] string? reason)
    {
        try
        {
            var payload = new CancelBookingRequest { Reason = reason };
            var resp = await _api.Client.PostAsJsonAsync($"/api/bookings/{id}/cancel", payload);

            if (resp.IsSuccessStatusCode)
            {
                var result = await resp.Content.ReadFromJsonAsync<CancelBookingResult>();
                SuccessMessage = result?.Message ?? "Booking cancelled successfully.";
            }
            else
            {
                var errorObj = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
                ErrorMessage = errorObj != null && errorObj.TryGetValue("error", out var msg)
                    ? msg
                    : "Unable to cancel booking.";
            }
        }
        catch
        {
            ErrorMessage = "Network failure while cancelling booking.";
        }

        return RedirectToPage(new { status = Status, sport = Sport, p = P });
    }
}

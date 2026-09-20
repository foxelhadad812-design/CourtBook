using CourtBook.Web.Models;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Courts;

public class BookModel : PageModel
{
    private readonly ApiClient _api;
    public BookModel(ApiClient api) => _api = api;

    public CourtResponse? Court { get; set; }
    
    [BindProperty]
    public DateTime BookingDate { get; set; } = DateTime.Today;

    [BindProperty]
    public string StartTime { get; set; } = "10:00";

    [BindProperty]
    public string EndTime { get; set; } = "11:00";

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid venueId, Guid id)
    {
        var response = await _api.Client.GetAsync($"/api/venues/{venueId}/courts/{id}");
        if (response.IsSuccessStatusCode)
        {
            Court = await response.Content.ReadFromJsonAsync<CourtResponse>();
            return Page();
        }
        return NotFound();
    }

    public async Task<IActionResult> OnPostAsync(Guid venueId, Guid id)
    {
        var startDateTime = BookingDate.Date + TimeSpan.Parse(StartTime);
        var endDateTime = BookingDate.Date + TimeSpan.Parse(EndTime);

        var request = new CreateBookingRequest
        {
            CourtId = id,
            StartTime = startDateTime,
            EndTime = endDateTime
        };

        var response = await _api.Client.PostAsJsonAsync("/api/bookings", request);
        
        if (response.IsSuccessStatusCode)
        {
            var bookingData = await response.Content.ReadFromJsonAsync<BookingResponse>();
            if(bookingData != null) 
            {
                // Pass booking ID via query or tempdata. TempData is easier for single redirect
                TempData["BookingId"] = bookingData.Id.ToString();
                TempData["CourtName"] = Court?.Name;
                TempData["StartTime"] = bookingData.StartTime.ToString("o");
                TempData["EndTime"] = bookingData.EndTime.ToString("o");
                TempData["TotalPrice"] = bookingData.TotalPrice.ToString("0.00");
                return RedirectToPage("/Bookings/Confirmation");
            }
            return RedirectToPage("/Bookings/Confirmation");
        }

        TempData["ErrorMessage"] = await response.Content.ReadAsStringAsync();
        
        // Reload court data for the view
        var courtRes = await _api.Client.GetAsync($"/api/venues/{venueId}/courts/{id}");
        if (courtRes.IsSuccessStatusCode)
        {
            Court = await courtRes.Content.ReadFromJsonAsync<CourtResponse>();
        }

        return Page();
    }
}

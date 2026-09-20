using CourtBook.Web.Models;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Bookings;

public class MyBookingsModel : PageModel
{
    private readonly ApiClient _api;
    public MyBookingsModel(ApiClient api) => _api = api;

    public List<BookingResponse> Bookings { get; set; } = [];

    [BindProperty(SupportsGet = true)]
    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        var response = await _api.Client.GetFromJsonAsync<List<BookingResponse>>("/api/bookings/my");
        if (response != null) Bookings = response;
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid id)
    {
        var response = await _api.Client.PutAsync($"/api/bookings/{id}/cancel", null);
        if (!response.IsSuccessStatusCode)
        {
            ErrorMessage = "Failed to cancel booking.";
        }
        return RedirectToPage();
    }
}

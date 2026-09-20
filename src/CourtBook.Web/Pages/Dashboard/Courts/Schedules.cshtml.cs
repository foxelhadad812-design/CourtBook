using CourtBook.Web.Models;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Dashboard.Courts;

public class SchedulesModel : PageModel
{
    private readonly ApiClient _api;
    public SchedulesModel(ApiClient api) => _api = api;

    [BindProperty(SupportsGet = true)] public Guid VenueId { get; set; }
    [BindProperty(SupportsGet = true)] public Guid CourtId { get; set; }
    
    public CourtResponse? Court { get; set; }

    [BindProperty] public CreateScheduleRequest Input { get; set; } = new();
    
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var response = await _api.Client.GetAsync($"/api/venues/{VenueId}/courts/{CourtId}");
        if (response.IsSuccessStatusCode)
        {
            Court = await response.Content.ReadFromJsonAsync<CourtResponse>();
            return Page();
        }
        return NotFound();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var response = await _api.Client.PostAsJsonAsync($"/api/venues/{VenueId}/courts/{CourtId}/schedules", Input);
        if (response.IsSuccessStatusCode)
        {
            return RedirectToPage(new { VenueId, CourtId });
        }
        ErrorMessage = "Failed to add schedule.";
        return await OnGetAsync();
    }
}

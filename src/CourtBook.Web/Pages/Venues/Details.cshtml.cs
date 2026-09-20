using CourtBook.Web.Models;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Venues;

public class DetailsModel : PageModel
{
    private readonly ApiClient _api;
    public DetailsModel(ApiClient api) => _api = api;

    public VenueResponse? Venue { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var response = await _api.Client.GetAsync($"/api/venues/{id}");
        if (response.IsSuccessStatusCode)
        {
            Venue = await response.Content.ReadFromJsonAsync<VenueResponse>();
            return Page();
        }
        return NotFound();
    }
}

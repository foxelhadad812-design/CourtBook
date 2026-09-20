using CourtBook.Web.Models;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages;

public class IndexModel : PageModel
{
    private readonly ApiClient _api;
    public IndexModel(ApiClient api) => _api = api;

    public List<VenueResponse> Venues { get; set; } = [];
    
    [BindProperty(SupportsGet = true)]
    public string? SearchCity { get; set; }

    public async Task OnGetAsync()
    {
        var response = await _api.Client.GetFromJsonAsync<List<VenueResponse>>("/api/venues");
        if (response != null)
        {
            Venues = string.IsNullOrEmpty(SearchCity) 
                ? response 
                : response.Where(v => v.City.Contains(SearchCity, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }
}

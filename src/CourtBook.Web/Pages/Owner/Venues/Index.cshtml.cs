using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Owner.Venues;

public class IndexModel : PageModel
{
    private readonly ApiClient _api;

    public IndexModel(ApiClient api)
    {
        _api = api;
    }

    public List<OwnerVenueDto> Venues { get; set; } = [];
    public bool IsOwner { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var role = HttpContext.Session.GetString("UserRole");
        if (role != "Owner" && role != "Admin")
        {
            IsOwner = false;
            return Page();
        }

        try
        {
            var response = await _api.Client.GetAsync("/api/owner/venues");
            if (response.IsSuccessStatusCode)
            {
                Venues = await response.Content.ReadFromJsonAsync<List<OwnerVenueDto>>() ?? [];
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                IsOwner = false;
            }
            else
            {
                ErrorMessage = "Unable to load managed facilities at this time.";
            }
        }
        catch
        {
            ErrorMessage = "Connection to facilities service failed.";
        }

        return Page();
    }
}

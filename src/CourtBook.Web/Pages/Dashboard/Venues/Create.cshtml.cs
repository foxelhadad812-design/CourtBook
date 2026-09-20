using CourtBook.Web.Models;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Dashboard.Venues;

public class CreateModel : PageModel
{
    private readonly ApiClient _api;
    public CreateModel(ApiClient api) => _api = api;

    [BindProperty] public CreateVenueRequest Input { get; set; } = new();
    public string? ErrorMessage { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        var response = await _api.Client.PostAsJsonAsync("/api/venues", Input);
        if (response.IsSuccessStatusCode)
        {
            return RedirectToPage("/Dashboard/Index");
        }
        ErrorMessage = "Failed to create venue.";
        return Page();
    }
}

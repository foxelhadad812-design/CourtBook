using CourtBook.Application.DTOs;
using CourtBook.Web.Models;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Dashboard.Courts;

public class CreateModel : PageModel
{
    private readonly ApiClient _api;
    public CreateModel(ApiClient api) => _api = api;

    [BindProperty] public CreateCourtRequest Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public Guid VenueId { get; set; }
    public string? ErrorMessage { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        Input.IsActive = true;
        var response = await _api.Client.PostAsJsonAsync($"/api/venues/{VenueId}/courts", Input);
        if (response.IsSuccessStatusCode)
        {
            return RedirectToPage("/Dashboard/Index");
        }
        ErrorMessage = "Failed to add court.";
        return Page();
    }
}

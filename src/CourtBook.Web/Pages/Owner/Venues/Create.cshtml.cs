using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Owner.Venues;

public class CreateModel : PageModel
{
    private readonly ApiClient _api;

    public CreateModel(ApiClient api)
    {
        _api = api;
    }

    [BindProperty]
    public CreateVenueRequest Input { get; set; } = new();

    public List<AmenityDto> AvailableAmenities { get; set; } = [];
    public string? ErrorMessage { get; set; }
    public bool IsOwner { get; set; } = true;

    public async Task<IActionResult> OnGetAsync()
    {
        var role = HttpContext.Session.GetString("UserRole");
        if (role != "Owner" && role != "Admin")
        {
            IsOwner = false;
            return Page();
        }

        await LoadAmenitiesAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var role = HttpContext.Session.GetString("UserRole");
        if (role != "Owner" && role != "Admin")
        {
            IsOwner = false;
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Input.Name) || string.IsNullOrWhiteSpace(Input.City) || string.IsNullOrWhiteSpace(Input.Address))
        {
            ErrorMessage = "Facility name, City, and Address are required.";
            await LoadAmenitiesAsync();
            return Page();
        }

        try
        {
            var response = await _api.Client.PostAsJsonAsync("/api/owner/venues", Input);
            if (response.IsSuccessStatusCode)
            {
                var created = await response.Content.ReadFromJsonAsync<OwnerVenueDto>();
                if (created != null)
                {
                    return Redirect($"/Owner/Venues/Manage?id={created.Id}&created=true");
                }
                return RedirectToPage("/Owner/Venues/Index");
            }

            var errJson = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            ErrorMessage = errJson != null && errJson.TryGetValue("error", out var msg)
                ? msg
                : "Failed to create facility. Please check your entries.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error communicating with server: {ex.Message}";
        }

        await LoadAmenitiesAsync();
        return Page();
    }

    private async Task LoadAmenitiesAsync()
    {
        try
        {
            var resp = await _api.Client.GetAsync("/api/owner/amenities");
            if (resp.IsSuccessStatusCode)
            {
                AvailableAmenities = await resp.Content.ReadFromJsonAsync<List<AmenityDto>>() ?? [];
            }
        }
        catch
        {
            AvailableAmenities = [];
        }
    }
}

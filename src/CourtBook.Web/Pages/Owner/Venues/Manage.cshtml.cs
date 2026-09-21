using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Owner.Venues;

public class ManageModel : PageModel
{
    private readonly ApiClient _api;

    public ManageModel(ApiClient api)
    {
        _api = api;
    }

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string ActiveTab { get; set; } = "details";

    public OwnerVenueDetailsDto? Venue { get; set; }
    public List<AmenityDto> AvailableAmenities { get; set; } = [];

    // Form Binds
    [BindProperty]
    public UpdateVenueRequest DetailsInput { get; set; } = new();

    [BindProperty]
    public CreateCourtRequest NewCourtInput { get; set; } = new();

    [BindProperty]
    public UpdateCourtRequest EditCourtInput { get; set; } = new();

    [BindProperty]
    public AddVenueImageRequest NewImageInput { get; set; } = new();

    [BindProperty]
    public List<Guid> SelectedAmenityIds { get; set; } = [];

    public bool IsOwner { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(bool? created)
    {
        var role = HttpContext.Session.GetString("UserRole");
        if (role != "Owner" && role != "Admin")
        {
            IsOwner = false;
            return Page();
        }

        if (created == true)
        {
            SuccessMessage = "Facility registered successfully! You can now add courts and customize amenities.";
            ActiveTab = "courts";
        }

        await LoadDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateDetailsAsync()
    {
        ActiveTab = "details";
        try
        {
            var resp = await _api.Client.PutAsJsonAsync($"/api/owner/venues/{Id}", DetailsInput);
            if (resp.IsSuccessStatusCode)
            {
                SuccessMessage = "Facility details updated successfully.";
            }
            else
            {
                var err = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
                ErrorMessage = err != null && err.TryGetValue("error", out var msg) ? msg : "Failed to update facility details.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error updating details: {ex.Message}";
        }

        await LoadDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeactivateFacilityAsync()
    {
        ActiveTab = "details";
        try
        {
            var resp = await _api.Client.PostAsync($"/api/owner/venues/{Id}/deactivate", null);
            var result = await resp.Content.ReadFromJsonAsync<DeactivateResultDto>();
            if (resp.IsSuccessStatusCode && result != null && result.Success)
            {
                SuccessMessage = "Facility has been deactivated successfully.";
            }
            else
            {
                ErrorMessage = result?.Message ?? "Cannot deactivate facility due to active reservations.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error deactivating facility: {ex.Message}";
        }

        await LoadDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateCourtAsync()
    {
        ActiveTab = "courts";
        try
        {
            var resp = await _api.Client.PostAsJsonAsync($"/api/owner/venues/{Id}/courts", NewCourtInput);
            if (resp.IsSuccessStatusCode)
            {
                SuccessMessage = $"Court '{NewCourtInput.Name}' added successfully!";
                NewCourtInput = new CreateCourtRequest();
            }
            else
            {
                var err = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
                ErrorMessage = err != null && err.TryGetValue("error", out var msg) ? msg : "Failed to add court.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error adding court: {ex.Message}";
        }

        await LoadDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateCourtAsync(Guid courtId)
    {
        ActiveTab = "courts";
        try
        {
            var resp = await _api.Client.PutAsJsonAsync($"/api/owner/courts/{courtId}", EditCourtInput);
            if (resp.IsSuccessStatusCode)
            {
                SuccessMessage = "Court updated successfully.";
            }
            else
            {
                var err = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
                ErrorMessage = err != null && err.TryGetValue("error", out var msg) ? msg : "Failed to update court.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error updating court: {ex.Message}";
        }

        await LoadDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeactivateCourtAsync(Guid courtId)
    {
        ActiveTab = "courts";
        try
        {
            var resp = await _api.Client.PostAsync($"/api/owner/courts/{courtId}/deactivate", null);
            var result = await resp.Content.ReadFromJsonAsync<DeactivateResultDto>();
            if (resp.IsSuccessStatusCode && result != null && result.Success)
            {
                SuccessMessage = "Court deactivated successfully.";
            }
            else
            {
                ErrorMessage = result?.Message ?? "Cannot deactivate court due to active upcoming bookings.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error deactivating court: {ex.Message}";
        }

        await LoadDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateAmenitiesAsync()
    {
        ActiveTab = "amenities";
        try
        {
            var resp = await _api.Client.PutAsJsonAsync($"/api/owner/venues/{Id}/amenities", SelectedAmenityIds);
            if (resp.IsSuccessStatusCode)
            {
                SuccessMessage = "Amenities saved successfully.";
            }
            else
            {
                ErrorMessage = "Failed to update amenities.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error updating amenities: {ex.Message}";
        }

        await LoadDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAddImageAsync()
    {
        ActiveTab = "images";
        try
        {
            var resp = await _api.Client.PostAsJsonAsync($"/api/owner/venues/{Id}/images", NewImageInput);
            if (resp.IsSuccessStatusCode)
            {
                SuccessMessage = "Image added successfully!";
                NewImageInput = new AddVenueImageRequest();
            }
            else
            {
                var err = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
                ErrorMessage = err != null && err.TryGetValue("error", out var msg) ? msg : "Failed to add image.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error adding image: {ex.Message}";
        }

        await LoadDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteImageAsync(Guid imageId)
    {
        ActiveTab = "images";
        try
        {
            var resp = await _api.Client.DeleteAsync($"/api/owner/venues/{Id}/images/{imageId}");
            if (resp.IsSuccessStatusCode)
            {
                SuccessMessage = "Image deleted successfully.";
            }
            else
            {
                ErrorMessage = "Failed to delete image.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error deleting image: {ex.Message}";
        }

        await LoadDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSetPrimaryImageAsync(Guid imageId)
    {
        ActiveTab = "images";
        try
        {
            var resp = await _api.Client.PutAsync($"/api/owner/venues/{Id}/images/{imageId}/primary", null);
            if (resp.IsSuccessStatusCode)
            {
                SuccessMessage = "Primary image updated successfully.";
            }
            else
            {
                ErrorMessage = "Failed to set primary image.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error updating primary image: {ex.Message}";
        }

        await LoadDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateOperatingHoursAsync([FromForm] List<UpdateOperatingHourRequest> hours)
    {
        ActiveTab = "hours";
        try
        {
            var resp = await _api.Client.PutAsJsonAsync($"/api/owner/venues/{Id}/operating-hours", hours);
            if (resp.IsSuccessStatusCode)
            {
                SuccessMessage = "Operating schedule updated successfully.";
            }
            else
            {
                ErrorMessage = "Failed to update operating hours.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error updating operating hours: {ex.Message}";
        }

        await LoadDataAsync();
        return Page();
    }

    private async Task LoadDataAsync()
    {
        try
        {
            var resp = await _api.Client.GetAsync($"/api/owner/venues/{Id}/details");
            if (resp.IsSuccessStatusCode)
            {
                Venue = await resp.Content.ReadFromJsonAsync<OwnerVenueDetailsDto>();
                if (Venue != null)
                {
                    DetailsInput = new UpdateVenueRequest
                    {
                        Name = Venue.Name,
                        Description = Venue.Description,
                        City = Venue.City,
                        Area = Venue.Area,
                        Address = Venue.Address,
                        Phone = Venue.Phone,
                        Email = Venue.Email,
                        Website = Venue.Website,
                        Latitude = Venue.Latitude,
                        Longitude = Venue.Longitude,
                        IsActive = Venue.IsActive
                    };
                    SelectedAmenityIds = Venue.Amenities.Select(a => a.Id).ToList();
                }
            }
            else if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                IsOwner = false;
            }
            else if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Venue = null;
            }

            var amenResp = await _api.Client.GetAsync("/api/owner/amenities");
            if (amenResp.IsSuccessStatusCode)
            {
                AvailableAmenities = await amenResp.Content.ReadFromJsonAsync<List<AmenityDto>>() ?? [];
                if (Venue != null)
                {
                    var venueAmenityIds = Venue.Amenities.Select(a => a.Id).ToHashSet();
                    foreach (var a in AvailableAmenities)
                    {
                        a.IsSelected = venueAmenityIds.Contains(a.Id);
                    }
                }
            }
        }
        catch
        {
            ErrorMessage = "Failed to communicate with facility service.";
        }
    }
}

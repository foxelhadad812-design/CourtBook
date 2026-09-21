using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Profile;

public class IndexModel : PageModel
{
    private readonly ApiClient _api;

    public IndexModel(ApiClient api)
    {
        _api = api;
    }

    public UserProfileResponse? Profile { get; set; }

    [BindProperty]
    public UpdateProfileRequest Form { get; set; } = new();

    [BindProperty]
    public string ActiveTab { get; set; } = "overview";

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(string? tab)
    {
        if (!string.IsNullOrWhiteSpace(tab))
        {
            ActiveTab = tab;
        }

        try
        {
            var resp = await _api.Client.GetAsync("/api/profile");
            if (resp.IsSuccessStatusCode)
            {
                Profile = await resp.Content.ReadFromJsonAsync<UserProfileResponse>();
                if (Profile != null)
                {
                    Form.Name = Profile.Name;
                    Form.Phone = Profile.Phone;
                    Form.Bio = Profile.Bio;
                    Form.SkillLevel = Profile.SkillLevel;
                    Form.PreferredSport = Profile.PreferredSport;
                    Form.PreferredCities = Profile.Preferences?.PreferredCities ?? [];
                    Form.PreferredDays = Profile.Preferences?.PreferredDays ?? [];
                    Form.PreferredTimeOfDay = Profile.Preferences?.PreferredTimeOfDay ?? [];
                }
                return Page();
            }

            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return RedirectToPage("/Login", new { returnUrl = "/Profile" });
            }

            ErrorMessage = "Failed to load profile details.";
        }
        catch
        {
            ErrorMessage = "Network error connecting to profile service.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        if (string.IsNullOrWhiteSpace(Form.Name))
        {
            ModelState.AddModelError("Form.Name", "Full Name is required.");
            ActiveTab = "edit";
            return await OnGetAsync("edit");
        }

        try
        {
            var resp = await _api.Client.PutAsJsonAsync("/api/profile", Form);
            if (resp.IsSuccessStatusCode)
            {
                SuccessMessage = "Profile and preferences updated successfully!";
                return RedirectToPage(new { tab = "overview" });
            }

            var errObj = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            ErrorMessage = errObj != null && errObj.TryGetValue("error", out var msg)
                ? msg
                : "Unable to update profile.";
        }
        catch
        {
            ErrorMessage = "Network error while saving profile.";
        }

        ActiveTab = "edit";
        return await OnGetAsync("edit");
    }
}

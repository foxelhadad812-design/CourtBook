using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Admin;

public class PromoCodesModel : PageModel
{
    private readonly ApiClient _api;

    public PromoCodesModel(ApiClient api)
    {
        _api = api;
    }

    public List<PromoCodeDto> PromoCodes { get; set; } = [];

    [BindProperty]
    public CreatePromoCodeRequest NewPromo { get; set; } = new();

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var role = HttpContext.Session.GetString("UserRole");
        if (string.IsNullOrEmpty(role))
        {
            return RedirectToPage("/Login", new { returnUrl = "/Admin/PromoCodes" });
        }

        if (!role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToPage("/AccessDenied");
        }

        await LoadDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var role = HttpContext.Session.GetString("UserRole");
        if (string.IsNullOrEmpty(role) || !role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToPage("/AccessDenied");
        }

        try
        {
            NewPromo.Code = NewPromo.Code.Trim().ToUpperInvariant();
            var resp = await _api.Client.PostAsJsonAsync("/api/promocodes", NewPromo);

            if (resp.IsSuccessStatusCode)
            {
                SuccessMessage = $"Promo code '{NewPromo.Code}' created successfully!";
                NewPromo = new CreatePromoCodeRequest();
            }
            else
            {
                var errObj = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
                ErrorMessage = errObj != null && errObj.TryGetValue("error", out var msg)
                    ? msg
                    : "Failed to create promo code.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error creating promo code: {ex.Message}";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeactivateAsync(Guid id)
    {
        var role = HttpContext.Session.GetString("UserRole");
        if (string.IsNullOrEmpty(role) || !role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToPage("/AccessDenied");
        }

        try
        {
            var resp = await _api.Client.DeleteAsync($"/api/promocodes/{id}");
            if (resp.IsSuccessStatusCode)
            {
                SuccessMessage = "Promo code deactivated successfully.";
            }
            else
            {
                ErrorMessage = "Failed to deactivate promo code.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error deactivating promo code: {ex.Message}";
        }

        return RedirectToPage();
    }

    private async Task LoadDataAsync()
    {
        try
        {
            var resp = await _api.Client.GetAsync("/api/promocodes");
            if (resp.IsSuccessStatusCode)
            {
                PromoCodes = await resp.Content.ReadFromJsonAsync<List<PromoCodeDto>>() ?? [];
            }
            else
            {
                ErrorMessage = "Could not load promo codes.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Network error: {ex.Message}";
        }
    }
}

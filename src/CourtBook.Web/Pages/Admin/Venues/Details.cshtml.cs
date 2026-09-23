using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Admin.Venues;

public class DetailsModel : PageModel
{
    private readonly ApiClient _api;

    public DetailsModel(ApiClient api)
    {
        _api = api;
    }

    public AdminVenueDetailsDto? Venue { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        try
        {
            var resp = await _api.Client.GetAsync($"/api/admin/venues/{id}");

            if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return RedirectToPage("/AccessDenied");

            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return RedirectToPage("/Login", new { returnUrl = $"/Admin/Venues/Details?id={id}" });

            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                return RedirectToPage("/NotFound");

            if (resp.IsSuccessStatusCode)
            {
                Venue = await resp.Content.ReadFromJsonAsync<AdminVenueDetailsDto>(ApiClient.JsonOptions);
            }
            else
            {
                ErrorMessage = "Could not load facility details.";
            }
        }
        catch (Exception)
        {
            ErrorMessage = "An unexpected error occurred while communicating with the server.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostApproveAsync(Guid id)
    {
        try
        {
            var resp = await _api.Client.PostAsync($"/api/admin/venues/{id}/approve", null);
            if (resp.IsSuccessStatusCode)
            {
                TempData["SuccessMessage"] = "Facility approved successfully and is now active on the marketplace.";
                return RedirectToPage(new { id });
            }

            ErrorMessage = "Failed to approve facility.";
        }
        catch (Exception)
        {
            ErrorMessage = "An unexpected error occurred while approving facility.";
        }

        return await OnGetAsync(id);
    }

    public async Task<IActionResult> OnPostRejectAsync(Guid id, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            ErrorMessage = "A rejection reason is required.";
            return await OnGetAsync(id);
        }

        try
        {
            var resp = await _api.Client.PostAsJsonAsync($"/api/admin/venues/{id}/reject", new RejectVenueRequest { Reason = reason.Trim() });
            if (resp.IsSuccessStatusCode)
            {
                TempData["SuccessMessage"] = "Facility rejected. The owner has been notified with your feedback.";
                return RedirectToPage(new { id });
            }

            ErrorMessage = "Failed to reject facility.";
        }
        catch (Exception)
        {
            ErrorMessage = "An unexpected error occurred while rejecting facility.";
        }

        return await OnGetAsync(id);
    }
}

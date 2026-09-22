using CourtBook.Application.DTOs;
using CourtBook.Domain.Enums;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Admin.Venues;

public class IndexModel : PageModel
{
    private readonly ApiClient _api;

    public IndexModel(ApiClient api)
    {
        _api = api;
    }

    public List<AdminVenueDto> Venues { get; set; } = [];

    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = "all";

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public int TotalCount { get; set; }
    public int PendingCount { get; set; }
    public int ApprovedCount { get; set; }
    public int RejectedCount { get; set; }

    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        try
        {
            // 1. Fetch counts across all statuses
            var allResp = await _api.Client.GetAsync("/api/admin/venues");
            if (allResp.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return RedirectToPage("/AccessDenied");
            if (allResp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return RedirectToPage("/Login", new { returnUrl = "/Admin/Venues" });

            if (allResp.IsSuccessStatusCode)
            {
                var allVenues = await allResp.Content.ReadFromJsonAsync<List<AdminVenueDto>>() ?? [];
                TotalCount = allVenues.Count;
                PendingCount = allVenues.Count(v => v.ApprovalStatus == VenueApprovalStatus.Pending);
                ApprovedCount = allVenues.Count(v => v.ApprovalStatus == VenueApprovalStatus.Approved);
                RejectedCount = allVenues.Count(v => v.ApprovalStatus == VenueApprovalStatus.Rejected);
            }

            // 2. Query filtered venues
            var queryUrl = "/api/admin/venues?";
            if (!string.IsNullOrWhiteSpace(Status) && !Status.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                queryUrl += $"status={Status}&";
            }
            if (!string.IsNullOrWhiteSpace(Search))
            {
                queryUrl += $"search={Uri.EscapeDataString(Search)}&";
            }

            var resp = await _api.Client.GetAsync(queryUrl);
            if (resp.IsSuccessStatusCode)
            {
                Venues = await resp.Content.ReadFromJsonAsync<List<AdminVenueDto>>() ?? [];
            }
            else
            {
                ErrorMessage = "Could not load facilities list.";
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
                SuccessMessage = "Facility approved successfully.";
            }
            else
            {
                ErrorMessage = "Failed to approve facility.";
            }
        }
        catch (Exception)
        {
            ErrorMessage = "An unexpected error occurred while approving facility.";
        }

        return RedirectToPage(new { status = Status, search = Search });
    }

    public async Task<IActionResult> OnPostRejectAsync(Guid id, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            ErrorMessage = "A rejection reason is required.";
            return await OnGetAsync();
        }

        try
        {
            var resp = await _api.Client.PostAsJsonAsync($"/api/admin/venues/{id}/reject", new RejectVenueRequest { Reason = reason.Trim() });
            if (resp.IsSuccessStatusCode)
            {
                SuccessMessage = "Facility rejected.";
            }
            else
            {
                ErrorMessage = "Failed to reject facility.";
            }
        }
        catch (Exception)
        {
            ErrorMessage = "An unexpected error occurred while rejecting facility.";
        }

        return RedirectToPage(new { status = Status, search = Search });
    }
}

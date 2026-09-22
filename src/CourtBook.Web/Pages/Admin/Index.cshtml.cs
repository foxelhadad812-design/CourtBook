using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Admin;

public class IndexModel : PageModel
{
    private readonly ApiClient _api;

    public IndexModel(ApiClient api)
    {
        _api = api;
    }

    public AdminDashboardDto? Dashboard { get; set; }
    public SettlementSummaryDto? SettlementSummary { get; set; }
    public AdminPayoutSummaryDto? PayoutSummary { get; set; }
    public RecoverySummaryDto? RecoverySummary { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        try
        {
            var resp = await _api.Client.GetAsync("/api/admin/dashboard");

            if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return RedirectToPage("/AccessDenied");
            }

            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return RedirectToPage("/Login", new { returnUrl = "/Admin/Index" });
            }

            if (resp.IsSuccessStatusCode)
            {
                Dashboard = await resp.Content.ReadFromJsonAsync<AdminDashboardDto>();
            }
            else
            {
                ErrorMessage = "Could not load admin statistics.";
            }

            // Load financial subsystem summaries
            try
            {
                var sResp = await _api.Client.GetAsync("/api/admin/settlements/summary");
                if (sResp.IsSuccessStatusCode) SettlementSummary = await sResp.Content.ReadFromJsonAsync<SettlementSummaryDto>();

                var pResp = await _api.Client.GetAsync("/api/admin/payouts/summary");
                if (pResp.IsSuccessStatusCode) PayoutSummary = await pResp.Content.ReadFromJsonAsync<AdminPayoutSummaryDto>();

                var rResp = await _api.Client.GetAsync("/api/admin/financial/recovery-obligations/summary");
                if (rResp.IsSuccessStatusCode) RecoverySummary = await rResp.Content.ReadFromJsonAsync<RecoverySummaryDto>();
            }
            catch
            {
                // Non-fatal for dashboard load
            }
        }
        catch (Exception)
        {
            ErrorMessage = "An unexpected error occurred while communicating with the server.";
        }

        return Page();
    }
}

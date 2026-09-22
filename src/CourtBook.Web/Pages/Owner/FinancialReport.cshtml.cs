using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Owner;

/// <summary>Owner Financial Report page — strictly scoped to logged-in owner's venues.</summary>
public class FinancialReportModel : PageModel
{
    private readonly ApiClient _api;

    public FinancialReportModel(ApiClient api) => _api = api;

    public OwnerFinancialReportDto? Report { get; set; }
    public string? ErrorMessage { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? To { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        try
        {
            var query = $"/api/payments/owner/report";
            var qs = new List<string>();
            if (From.HasValue) qs.Add($"from={From.Value:yyyy-MM-dd}");
            if (To.HasValue)   qs.Add($"to={To.Value:yyyy-MM-dd}");
            if (qs.Count > 0) query += "?" + string.Join("&", qs);

            var resp = await _api.Client.GetAsync(query);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return RedirectToPage("/Login", new { returnUrl = "/Owner/FinancialReport" });

            if (resp.IsSuccessStatusCode)
            {
                Report = await resp.Content.ReadFromJsonAsync<OwnerFinancialReportDto>();
            }
            else
            {
                ErrorMessage = "Unable to load financial report.";
            }
        }
        catch
        {
            ErrorMessage = "Network error loading financial report.";
        }

        return Page();
    }
}

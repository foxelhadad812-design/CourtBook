using System.Net.Http.Json;
using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Admin;

public class RecoveryModel : PageModel
{
    private readonly ApiClient _api;

    public RecoveryModel(ApiClient api)
    {
        _api = api;
    }

    public RecoverySummaryDto? Summary { get; set; }
    public PagedResult<RecoveryObligationDto> PagedObligations { get; set; } = PagedResult<RecoveryObligationDto>.Empty();

    [BindProperty(SupportsGet = true)]
    public int P { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = "all";

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var role = HttpContext.Session.GetString("UserRole");
        if (string.IsNullOrEmpty(role))
        {
            return RedirectToPage("/Login", new { returnUrl = "/Admin/Recovery" });
        }

        if (!role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToPage("/AccessDenied");
        }

        await LoadDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSettleManuallyAsync(Guid id, [FromForm] ManualSettleRecoveryRequest request)
    {
        var role = HttpContext.Session.GetString("UserRole");
        if (string.IsNullOrEmpty(role))
        {
            return RedirectToPage("/Login", new { returnUrl = "/Admin/Recovery" });
        }

        if (!role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToPage("/AccessDenied");
        }

        if (request.Amount <= 0)
        {
            ErrorMessage = "Settlement amount must be greater than zero.";
            return RedirectToPage(new { p = P, status = Status });
        }

        if (string.IsNullOrWhiteSpace(request.ExternalReference))
        {
            ErrorMessage = "External reference or receipt number is required.";
            return RedirectToPage(new { p = P, status = Status });
        }

        try
        {
            var resp = await _api.Client.PostAsJsonAsync($"/api/admin/financial/recovery-obligations/{id}/settle-manually", request);
            if (resp.IsSuccessStatusCode)
            {
                SuccessMessage = $"Manual recovery of EGP {request.Amount:0.00} recorded successfully. Owner deficit updated.";
            }
            else
            {
                var err = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
                ErrorMessage = err?.Error ?? "Failed to settle recovery obligation.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Network error: {ex.Message}";
        }

        return RedirectToPage(new { p = P, status = Status });
    }

    public async Task<IActionResult> OnPostWriteOffAsync(Guid id, [FromForm] WriteOffRecoveryRequest request)
    {
        var role = HttpContext.Session.GetString("UserRole");
        if (string.IsNullOrEmpty(role))
        {
            return RedirectToPage("/Login", new { returnUrl = "/Admin/Recovery" });
        }

        if (!role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToPage("/AccessDenied");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            ErrorMessage = "A reason is mandatory to write off a deficit obligation.";
            return RedirectToPage(new { p = P, status = Status });
        }

        try
        {
            var resp = await _api.Client.PostAsJsonAsync($"/api/admin/financial/recovery-obligations/{id}/write-off", request);
            if (resp.IsSuccessStatusCode)
            {
                SuccessMessage = "Obligation written off successfully and owner balance deficit relieved.";
            }
            else
            {
                var err = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
                ErrorMessage = err?.Error ?? "Failed to write off obligation.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Network error: {ex.Message}";
        }

        return RedirectToPage(new { p = P, status = Status });
    }

    private async Task LoadDataAsync()
    {
        try
        {
            // 1. Summary
            var sumResp = await _api.Client.GetAsync("/api/admin/financial/recovery-obligations/summary");
            if (sumResp.IsSuccessStatusCode)
            {
                Summary = await sumResp.Content.ReadFromJsonAsync<RecoverySummaryDto>();
            }

            // 2. Obligations
            var statusFilter = string.Equals(Status, "all", StringComparison.OrdinalIgnoreCase) ? "" : $"&status={Status}";
            var oblUrl = $"/api/admin/financial/recovery-obligations?page={P}&pageSize=15{statusFilter}";
            var oblResp = await _api.Client.GetAsync(oblUrl);
            if (oblResp.IsSuccessStatusCode)
            {
                PagedObligations = await oblResp.Content.ReadFromJsonAsync<PagedResult<RecoveryObligationDto>>() ?? PagedResult<RecoveryObligationDto>.Empty();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load recovery records: {ex.Message}";
        }
    }

    private sealed record ErrorResponse(string? Error);
}

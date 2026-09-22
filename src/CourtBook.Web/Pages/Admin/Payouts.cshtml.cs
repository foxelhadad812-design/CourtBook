using System.Net.Http.Json;
using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Admin;

public class PayoutsModel : PageModel
{
    private readonly ApiClient _api;

    public PayoutsModel(ApiClient api)
    {
        _api = api;
    }

    public AdminPayoutSummaryDto? Summary { get; set; }
    public PagedResult<PayoutRequestDto> PagedPayouts { get; set; } = PagedResult<PayoutRequestDto>.Empty();
    public Guid CurrentAdminId { get; set; }

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
            return RedirectToPage("/Login", new { returnUrl = "/Admin/Payouts" });
        }

        if (!role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToPage("/AccessDenied");
        }

        if (Guid.TryParse(HttpContext.Session.GetString("UserId"), out var adminId))
        {
            CurrentAdminId = adminId;
        }

        await LoadDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostApproveAsync(Guid id, Guid ownerId)
    {
        var role = HttpContext.Session.GetString("UserRole");
        if (string.IsNullOrEmpty(role))
        {
            return RedirectToPage("/Login", new { returnUrl = "/Admin/Payouts" });
        }

        if (!role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToPage("/AccessDenied");
        }

        if (Guid.TryParse(HttpContext.Session.GetString("UserId"), out var adminId))
        {
            CurrentAdminId = adminId;
        }

        // Anti-self-dealing check
        if (CurrentAdminId != Guid.Empty && ownerId == CurrentAdminId)
        {
            ErrorMessage = "Anti-self-dealing protection: Administrators are strictly prohibited from approving their own owner payout requests.";
            return RedirectToPage(new { p = P, status = Status });
        }

        try
        {
            var resp = await _api.Client.PostAsync($"/api/admin/payouts/{id}/approve", null);
            if (resp.IsSuccessStatusCode)
            {
                SuccessMessage = "Payout request approved successfully. It is now queued for manual disbursement.";
            }
            else
            {
                var err = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
                ErrorMessage = err?.Error ?? "Failed to approve payout request.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Network error: {ex.Message}";
        }

        return RedirectToPage(new { p = P, status = Status });
    }

    public async Task<IActionResult> OnPostRejectAsync(Guid id, [FromForm] RejectPayoutRequest request)
    {
        var role = HttpContext.Session.GetString("UserRole");
        if (string.IsNullOrEmpty(role))
        {
            return RedirectToPage("/Login", new { returnUrl = "/Admin/Payouts" });
        }

        if (!role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToPage("/AccessDenied");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            ErrorMessage = "Rejection reason is required.";
            return RedirectToPage(new { p = P, status = Status });
        }

        try
        {
            var resp = await _api.Client.PostAsJsonAsync($"/api/admin/payouts/{id}/reject", request);
            if (resp.IsSuccessStatusCode)
            {
                SuccessMessage = "Payout request rejected and reserved funds returned to Owner's Available Balance.";
            }
            else
            {
                var err = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
                ErrorMessage = err?.Error ?? "Failed to reject payout request.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Network error: {ex.Message}";
        }

        return RedirectToPage(new { p = P, status = Status });
    }

    public async Task<IActionResult> OnPostMarkPaidAsync(Guid id, [FromForm] MarkPayoutPaidRequest request)
    {
        var role = HttpContext.Session.GetString("UserRole");
        if (string.IsNullOrEmpty(role))
        {
            return RedirectToPage("/Login", new { returnUrl = "/Admin/Payouts" });
        }

        if (!role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToPage("/AccessDenied");
        }

        if (string.IsNullOrWhiteSpace(request.ExternalTransactionReference))
        {
            ErrorMessage = "External transaction reference is mandatory to record disbursement.";
            return RedirectToPage(new { p = P, status = Status });
        }

        try
        {
            var resp = await _api.Client.PostAsJsonAsync($"/api/admin/payouts/{id}/mark-paid", request);
            if (resp.IsSuccessStatusCode)
            {
                SuccessMessage = $"Payout disbursement confirmed. In-flight funds finalized and external reference '{request.ExternalTransactionReference}' recorded.";
            }
            else
            {
                var err = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
                ErrorMessage = err?.Error ?? "Failed to mark payout as paid.";
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
            var sumResp = await _api.Client.GetAsync("/api/admin/payouts/summary");
            if (sumResp.IsSuccessStatusCode)
            {
                Summary = await sumResp.Content.ReadFromJsonAsync<AdminPayoutSummaryDto>();
            }

            // 2. Payouts table
            var statusParam = string.Equals(Status, "all", StringComparison.OrdinalIgnoreCase) ? "" : $"&status={Status}";
            var payoutsUrl = $"/api/admin/payouts?page={P}&pageSize=15{statusParam}";
            var payResp = await _api.Client.GetAsync(payoutsUrl);
            if (payResp.IsSuccessStatusCode)
            {
                PagedPayouts = await payResp.Content.ReadFromJsonAsync<PagedResult<PayoutRequestDto>>() ?? PagedResult<PayoutRequestDto>.Empty();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load payout queue: {ex.Message}";
        }
    }

    private sealed record ErrorResponse(string? Error);
}

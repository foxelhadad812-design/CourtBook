using System.Net.Http.Json;
using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Owner;

public class PayoutsModel : PageModel
{
    private readonly ApiClient _api;

    public PayoutsModel(ApiClient api)
    {
        _api = api;
    }

    public OwnerBalanceDto? Balance { get; set; }
    public List<PayoutMethodDto> PayoutMethods { get; set; } = [];
    public PagedResult<PayoutRequestDto> PagedPayouts { get; set; } = PagedResult<PayoutRequestDto>.Empty();

    [BindProperty(SupportsGet = true)]
    public int P { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = "all";

    public bool IsForbidden { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var role = HttpContext.Session.GetString("UserRole");
        if (string.IsNullOrEmpty(role))
        {
            return RedirectToPage("/Login", new { returnUrl = "/Owner/Payouts" });
        }

        if (!role.Equals("Owner", StringComparison.OrdinalIgnoreCase))
        {
            IsForbidden = true;
            return Page();
        }

        await LoadDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAddMethodAsync([FromForm] CreatePayoutMethodRequest methodRequest)
    {
        try
        {
            var resp = await _api.Client.PostAsJsonAsync("/api/owner/payout-methods", methodRequest);
            if (resp.IsSuccessStatusCode)
            {
                SuccessMessage = "Payout destination method registered successfully.";
            }
            else
            {
                var err = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
                ErrorMessage = err?.Error ?? "Failed to add payout method. Please verify the account format.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Network error: {ex.Message}";
        }

        return RedirectToPage(new { p = P, status = Status });
    }

    public async Task<IActionResult> OnPostDeleteMethodAsync(Guid methodId)
    {
        try
        {
            var resp = await _api.Client.DeleteAsync($"/api/owner/payout-methods/{methodId}");
            if (resp.IsSuccessStatusCode)
            {
                SuccessMessage = "Payout method deactivated.";
            }
            else
            {
                var err = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
                ErrorMessage = err?.Error ?? "Could not deactivate payout method.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Network error: {ex.Message}";
        }

        return RedirectToPage(new { p = P, status = Status });
    }

    public async Task<IActionResult> OnPostSetDefaultMethodAsync(Guid methodId)
    {
        try
        {
            var resp = await _api.Client.PostAsync($"/api/owner/payout-methods/{methodId}/default", null);
            if (resp.IsSuccessStatusCode)
            {
                SuccessMessage = "Default payout method updated.";
            }
            else
            {
                var err = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
                ErrorMessage = err?.Error ?? "Could not set default payout method.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Network error: {ex.Message}";
        }

        return RedirectToPage(new { p = P, status = Status });
    }

    public async Task<IActionResult> OnPostRequestPayoutAsync([FromForm] CreatePayoutRequest payoutRequest)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(payoutRequest.IdempotencyKey))
            {
                payoutRequest.IdempotencyKey = Guid.NewGuid().ToString("N");
            }

            var resp = await _api.Client.PostAsJsonAsync("/api/owner/payouts/request", payoutRequest);
            if (resp.IsSuccessStatusCode)
            {
                SuccessMessage = $"Payout withdrawal request of EGP {payoutRequest.Amount:0.00} submitted successfully and is awaiting review.";
            }
            else
            {
                var err = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
                ErrorMessage = err?.Error ?? "Payout request was rejected. Please verify your available balance and active obligations.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Network error: {ex.Message}";
        }

        return RedirectToPage(new { p = P, status = Status });
    }

    public async Task<IActionResult> OnPostCancelPayoutAsync(Guid payoutId)
    {
        try
        {
            var resp = await _api.Client.PostAsync($"/api/owner/payouts/{payoutId}/cancel", null);
            if (resp.IsSuccessStatusCode)
            {
                SuccessMessage = "Payout request cancelled and funds returned to Available Balance.";
            }
            else
            {
                var err = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
                ErrorMessage = err?.Error ?? "Could not cancel payout request.";
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
            // 1. Balance
            var balResp = await _api.Client.GetAsync("/api/owner/balance");
            if (balResp.IsSuccessStatusCode)
            {
                Balance = await balResp.Content.ReadFromJsonAsync<OwnerBalanceDto>();
            }

            // 2. Methods
            var methResp = await _api.Client.GetAsync("/api/owner/payout-methods");
            if (methResp.IsSuccessStatusCode)
            {
                PayoutMethods = await methResp.Content.ReadFromJsonAsync<List<PayoutMethodDto>>() ?? [];
            }

            // 3. Payout Requests
            var statusFilter = string.Equals(Status, "all", StringComparison.OrdinalIgnoreCase) ? "" : $"&status={Status}";
            var payoutsUrl = $"/api/owner/payouts?page={P}&pageSize=10{statusFilter}";
            var payResp = await _api.Client.GetAsync(payoutsUrl);
            if (payResp.IsSuccessStatusCode)
            {
                PagedPayouts = await payResp.Content.ReadFromJsonAsync<PagedResult<PayoutRequestDto>>() ?? PagedResult<PayoutRequestDto>.Empty();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load financial records: {ex.Message}";
        }
    }

    private sealed record ErrorResponse(string? Error);
}

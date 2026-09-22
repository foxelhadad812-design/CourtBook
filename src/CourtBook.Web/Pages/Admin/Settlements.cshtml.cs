using System.Net.Http.Json;
using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Admin;

public class SettlementsModel : PageModel
{
    private readonly ApiClient _api;

    public SettlementsModel(ApiClient api)
    {
        _api = api;
    }

    public SettlementSummaryDto? Summary { get; set; }
    public PagedResult<SettlementBatchDto> PagedBatches { get; set; } = PagedResult<SettlementBatchDto>.Empty();
    public SettlementBatchDto? InspectedBatch { get; set; }

    [BindProperty(SupportsGet = true)]
    public int P { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public Guid? InspectBatchId { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var role = HttpContext.Session.GetString("UserRole");
        if (string.IsNullOrEmpty(role))
        {
            return RedirectToPage("/Login", new { returnUrl = "/Admin/Settlements" });
        }

        if (!role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToPage("/AccessDenied");
        }

        await LoadDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostTriggerAsync([FromForm] int bufferHours = 24)
    {
        var role = HttpContext.Session.GetString("UserRole");
        if (string.IsNullOrEmpty(role))
        {
            return RedirectToPage("/Login", new { returnUrl = "/Admin/Settlements" });
        }

        if (!role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToPage("/AccessDenied");
        }

        try
        {
            var req = new RunSettlementRequest { BufferHours = Math.Max(0, bufferHours) };
            var resp = await _api.Client.PostAsJsonAsync("/api/admin/settlements/run", req);

            if (resp.IsSuccessStatusCode)
            {
                var batch = await resp.Content.ReadFromJsonAsync<SettlementBatchDto>();
                if (batch is not null)
                {
                    SuccessMessage = $"Settlement batch '{batch.BatchReference}' executed successfully. Settled {batch.ItemCount} bookings for net EGP {batch.TotalNet:0.00}.";
                }
                else
                {
                    SuccessMessage = "Settlement sweep completed successfully.";
                }
            }
            else
            {
                var err = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
                ErrorMessage = err?.Error ?? "Settlement sweep execution failed.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Network error triggering settlement: {ex.Message}";
        }

        return RedirectToPage(new { p = 1 });
    }

    private async Task LoadDataAsync()
    {
        try
        {
            // 1. Summary
            var sumResp = await _api.Client.GetAsync("/api/admin/settlements/summary");
            if (sumResp.IsSuccessStatusCode)
            {
                Summary = await sumResp.Content.ReadFromJsonAsync<SettlementSummaryDto>();
            }

            // 2. Batches
            var batchesResp = await _api.Client.GetAsync($"/api/admin/settlements?page={P}&pageSize=15");
            if (batchesResp.IsSuccessStatusCode)
            {
                PagedBatches = await batchesResp.Content.ReadFromJsonAsync<PagedResult<SettlementBatchDto>>() ?? PagedResult<SettlementBatchDto>.Empty();
            }

            // 3. Optional batch inspection
            if (InspectBatchId.HasValue && InspectBatchId.Value != Guid.Empty)
            {
                var inspectResp = await _api.Client.GetAsync($"/api/admin/settlements/{InspectBatchId.Value}");
                if (inspectResp.IsSuccessStatusCode)
                {
                    InspectedBatch = await inspectResp.Content.ReadFromJsonAsync<SettlementBatchDto>();
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load settlement records: {ex.Message}";
        }
    }

    private sealed record ErrorResponse(string? Error);
}

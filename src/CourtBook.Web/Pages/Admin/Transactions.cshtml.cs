using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Admin;

/// <summary>Admin financial transaction history — Admin only.</summary>
public class TransactionsModel : PageModel
{
    private readonly ApiClient _api;

    public TransactionsModel(ApiClient api) => _api = api;

    public PagedResult<TransactionLedgerDto> PagedTransactions { get; set; } = PagedResult<TransactionLedgerDto>.Empty();
    public string? ErrorMessage { get; set; }

    [BindProperty(SupportsGet = true)]
    public int P { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? To { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        try
        {
            var qs = new List<string> { $"page={P}", "pageSize=25" };
            if (!string.IsNullOrWhiteSpace(Status)) qs.Add($"status={Status}");
            if (From.HasValue) qs.Add($"from={From.Value:yyyy-MM-dd}");
            if (To.HasValue)   qs.Add($"to={To.Value:yyyy-MM-dd}");

            var resp = await _api.Client.GetAsync($"/api/payments/admin/transactions?{string.Join("&", qs)}");
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return RedirectToPage("/Login", new { returnUrl = "/Admin/Transactions" });
            if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return RedirectToPage("/AccessDenied");

            if (resp.IsSuccessStatusCode)
            {
                var result = await resp.Content.ReadFromJsonAsync<PagedResult<TransactionLedgerDto>>();
                if (result is not null) PagedTransactions = result;
            }
            else
            {
                ErrorMessage = "Unable to load transactions.";
            }
        }
        catch
        {
            ErrorMessage = "Network error loading transactions.";
        }

        return Page();
    }
}

using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Owner;

public class IndexModel : PageModel
{
    private readonly ApiClient _api;

    public IndexModel(ApiClient api)
    {
        _api = api;
    }

    public OwnerDashboardSummaryDto? Dashboard { get; set; }
    public OwnerBalanceDto? Balance { get; set; }
    public PagedResult<OwnerBookingDto> PagedBookings { get; set; } = PagedResult<OwnerBookingDto>.Empty();

    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = "all";

    [BindProperty(SupportsGet = true)]
    public Guid? VenueId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int P { get; set; } = 1;

    public bool IsForbidden { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        try
        {
            // 1. Fetch Dashboard summary
            var dashResp = await _api.Client.GetAsync("/api/owner/dashboard");

            if (dashResp.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                IsForbidden = true;
                return Page();
            }

            if (dashResp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return RedirectToPage("/Login", new { returnUrl = "/Owner" });
            }

            if (dashResp.IsSuccessStatusCode)
            {
                Dashboard = await dashResp.Content.ReadFromJsonAsync<OwnerDashboardSummaryDto>();
            }
            else
            {
                ErrorMessage = "Could not load dashboard statistics.";
            }

            // 1b. Fetch Wallet Balance
            var balResp = await _api.Client.GetAsync("/api/owner/balance");
            if (balResp.IsSuccessStatusCode)
            {
                Balance = await balResp.Content.ReadFromJsonAsync<OwnerBalanceDto>();
            }

            // 2. Fetch Paged Bookings with filters
            var venueQuery = VenueId.HasValue && VenueId.Value != Guid.Empty ? $"&venueId={VenueId.Value}" : "";
            var bookingsUrl = $"/api/owner/bookings?status={Status}{venueQuery}&page={P}&pageSize=8";

            var bookingsResp = await _api.Client.GetAsync(bookingsUrl);
            if (bookingsResp.IsSuccessStatusCode)
            {
                var paged = await bookingsResp.Content.ReadFromJsonAsync<PagedResult<OwnerBookingDto>>();
                if (paged != null)
                {
                    PagedBookings = paged;
                }
            }
        }
        catch
        {
            ErrorMessage = "Network error connecting to facility management services.";
        }

        return Page();
    }
}

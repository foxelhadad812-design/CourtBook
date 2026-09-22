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
        }
        catch (Exception)
        {
            ErrorMessage = "An unexpected error occurred while communicating with the server.";
        }

        return Page();
    }
}

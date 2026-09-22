using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages;

public class NotificationsModel : PageModel
{
    private readonly ApiClient _api;

    public NotificationsModel(ApiClient api)
    {
        _api = api;
    }

    public PagedResult<NotificationDto> Notifications { get; set; } = PagedResult<NotificationDto>.Empty();
    public int UnreadCount { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Filter { get; set; } = "all"; // "all" or "unread"

    [BindProperty(SupportsGet = true)]
    public int P { get; set; } = 1;

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        try
        {
            // 1. Fetch unread count
            var countResp = await _api.Client.GetAsync("/api/notifications/unread-count");
            if (countResp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return RedirectToPage("/Login", new { returnUrl = "/Notifications" });
            }

            if (countResp.IsSuccessStatusCode)
            {
                var countObj = await countResp.Content.ReadFromJsonAsync<UnreadCountResponse>();
                UnreadCount = countObj?.UnreadCount ?? 0;
            }

            // 2. Fetch notifications
            var resp = await _api.Client.GetAsync($"/api/notifications?page={P}&pageSize=15");
            if (resp.IsSuccessStatusCode)
            {
                var result = await resp.Content.ReadFromJsonAsync<PagedResult<NotificationDto>>();
                if (result != null)
                {
                    if (Filter.Equals("unread", StringComparison.OrdinalIgnoreCase))
                    {
                        var unreadItems = result.Items.Where(n => !n.IsRead).ToList();
                        Notifications = PagedResult<NotificationDto>.From(unreadItems, unreadItems.Count, result.Page, result.PageSize);
                    }
                    else
                    {
                        Notifications = result;
                    }
                }
            }
            else
            {
                ErrorMessage = "Could not load notifications.";
            }
        }
        catch (Exception)
        {
            ErrorMessage = "An unexpected error occurred while communicating with the server.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostMarkReadAsync(Guid id)
    {
        try
        {
            await _api.Client.PostAsync($"/api/notifications/{id}/read", null);
        }
        catch { }

        return RedirectToPage(new { filter = Filter, p = P });
    }

    public async Task<IActionResult> OnPostMarkAllReadAsync()
    {
        try
        {
            await _api.Client.PostAsync("/api/notifications/read-all", null);
        }
        catch { }

        return RedirectToPage(new { filter = Filter });
    }

    public bool IsSafeLocalUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return url.StartsWith("/") && !url.StartsWith("//") && !url.StartsWith("/\\");
    }

    private class UnreadCountResponse
    {
        public int UnreadCount { get; set; }
    }
}

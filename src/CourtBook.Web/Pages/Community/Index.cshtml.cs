using System.Net.Http.Json;
using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Domain.Enums;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Community;

public class IndexModel : PageModel
{
    private readonly ApiClient _api;
    private readonly ITextLocalizer _loc;

    public IndexModel(ApiClient api, ITextLocalizer loc)
    {
        _api = api;
        _loc = loc;
    }

    [BindProperty(SupportsGet = true)] public string ActiveTab { get; set; } = "players";
    [BindProperty(SupportsGet = true)] public string? Query { get; set; }
    [BindProperty(SupportsGet = true)] public string? Sport { get; set; }
    [BindProperty(SupportsGet = true)] public string? City { get; set; }

    public PagedResult<PublicPlayerProfileDto>? PlayersResult { get; set; }
    public PagedResult<PlayerConnectionDto>? ConnectionsResult { get; set; }
    public PagedResult<PlayerConnectionDto>? PendingRequestsResult { get; set; }
    public PagedResult<PlayerConnectionDto>? BlockedUsersResult { get; set; }

    [TempData] public string? SuccessMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var token = HttpContext.Session.GetString("JwtToken");
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Login", new { returnUrl = "/Community" });
        }

        switch (ActiveTab.ToLowerInvariant())
        {
            case "connections":
                await LoadConnectionsAsync();
                break;
            case "pending":
                await LoadPendingRequestsAsync();
                break;
            case "blocked":
                await LoadBlockedUsersAsync();
                break;
            default:
                await LoadPlayersAsync();
                break;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostConnectAsync(Guid targetUserId)
    {
        var res = await SendAuthorizedPostAsync("/api/connections", new SendConnectionRequest { TargetUserId = targetUserId });
        if (res.IsSuccessStatusCode)
        {
            SuccessMessage = _loc.IsArabic ? "تم إرسال طلب التواصل بنجاح!" : "Connection request sent successfully!";
        }
        else
        {
            ErrorMessage = _loc.IsArabic ? "تعذر إرسال طلب التواصل." : "Failed to send connection request.";
        }
        return RedirectToPage(new { ActiveTab, Query, Sport, City });
    }

    public async Task<IActionResult> OnPostAcceptAsync(Guid connectionId)
    {
        var res = await SendAuthorizedPostAsync($"/api/connections/{connectionId}/accept", null);
        if (res.IsSuccessStatusCode)
        {
            SuccessMessage = _loc.IsArabic ? "تم قبول التواصل بنجاح!" : "Connection accepted successfully!";
        }
        else
        {
            ErrorMessage = _loc.IsArabic ? "تعذر قبول التواصل." : "Failed to accept connection.";
        }
        return RedirectToPage(new { ActiveTab = "connections" });
    }

    public async Task<IActionResult> OnPostDeclineAsync(Guid connectionId)
    {
        var res = await SendAuthorizedPostAsync($"/api/connections/{connectionId}/decline", null);
        if (res.IsSuccessStatusCode)
        {
            SuccessMessage = _loc.IsArabic ? "تم رفض الطلب." : "Connection request declined.";
        }
        return RedirectToPage(new { ActiveTab = "pending" });
    }

    public async Task<IActionResult> OnPostRemoveAsync(Guid connectionId)
    {
        var res = await SendAuthorizedDeleteAsync($"/api/connections/{connectionId}");
        if (res.IsSuccessStatusCode)
        {
            SuccessMessage = _loc.IsArabic ? "تم حذف التواصل." : "Connection removed.";
        }
        return RedirectToPage(new { ActiveTab = "connections" });
    }

    public async Task<IActionResult> OnPostBlockAsync(Guid targetUserId)
    {
        var res = await SendAuthorizedPostAsync("/api/connections/block", new BlockUserRequest { TargetUserId = targetUserId });
        if (res.IsSuccessStatusCode)
        {
            SuccessMessage = _loc.IsArabic ? "تم حظر المستخدم." : "User blocked.";
        }
        return RedirectToPage(new { ActiveTab = "blocked" });
    }

    public async Task<IActionResult> OnPostUnblockAsync(Guid targetUserId)
    {
        var res = await SendAuthorizedPostAsync("/api/connections/unblock", new BlockUserRequest { TargetUserId = targetUserId });
        if (res.IsSuccessStatusCode)
        {
            SuccessMessage = _loc.IsArabic ? "تم إلغاء حظر المستخدم." : "User unblocked.";
        }
        return RedirectToPage(new { ActiveTab = "blocked" });
    }

    private async Task LoadPlayersAsync()
    {
        var q = $"/api/community/players/search?Query={Uri.EscapeDataString(Query ?? "")}&SportType={Uri.EscapeDataString(Sport ?? "")}&City={Uri.EscapeDataString(City ?? "")}";
        var res = await SendAuthorizedGetAsync(q);
        if (res.IsSuccessStatusCode)
        {
            PlayersResult = await res.Content.ReadFromJsonAsync<PagedResult<PublicPlayerProfileDto>>();
        }
    }

    private async Task LoadConnectionsAsync()
    {
        var res = await SendAuthorizedGetAsync("/api/connections?status=Accepted");
        if (res.IsSuccessStatusCode)
        {
            ConnectionsResult = await res.Content.ReadFromJsonAsync<PagedResult<PlayerConnectionDto>>();
        }
    }

    private async Task LoadPendingRequestsAsync()
    {
        var res = await SendAuthorizedGetAsync("/api/connections?status=Pending");
        if (res.IsSuccessStatusCode)
        {
            PendingRequestsResult = await res.Content.ReadFromJsonAsync<PagedResult<PlayerConnectionDto>>();
        }
    }

    private async Task LoadBlockedUsersAsync()
    {
        var res = await SendAuthorizedGetAsync("/api/connections/blocked");
        if (res.IsSuccessStatusCode)
        {
            BlockedUsersResult = await res.Content.ReadFromJsonAsync<PagedResult<PlayerConnectionDto>>();
        }
    }

    private async Task<HttpResponseMessage> SendAuthorizedGetAsync(string url)
    {
        var token = HttpContext.Session.GetString("JwtToken");
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(token))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await _api.Client.SendAsync(req);
    }

    private async Task<HttpResponseMessage> SendAuthorizedPostAsync(string url, object? content)
    {
        var token = HttpContext.Session.GetString("JwtToken");
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (content != null)
            req.Content = JsonContent.Create(content);
        if (!string.IsNullOrEmpty(token))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await _api.Client.SendAsync(req);
    }

    private async Task<HttpResponseMessage> SendAuthorizedDeleteAsync(string url)
    {
        var token = HttpContext.Session.GetString("JwtToken");
        var req = new HttpRequestMessage(HttpMethod.Delete, url);
        if (!string.IsNullOrEmpty(token))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await _api.Client.SendAsync(req);
    }
}

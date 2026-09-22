using System.Net.Http.Json;
using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Profile;

public class PublicModel : PageModel
{
    private readonly ApiClient _api;
    private readonly ITextLocalizer _loc;

    public PublicModel(ApiClient api, ITextLocalizer loc)
    {
        _api = api;
        _loc = loc;
    }

    public PublicPlayerProfileDto? Profile { get; set; }

    [TempData] public string? SuccessMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid userId)
    {
        var res = await SendAuthorizedGetAsync($"/api/community/players/{userId}/profile");
        if (res.IsSuccessStatusCode)
        {
            Profile = await res.Content.ReadFromJsonAsync<PublicPlayerProfileDto>();
            return Page();
        }

        return RedirectToPage("/Community");
    }

    public async Task<IActionResult> OnPostConnectAsync(Guid userId)
    {
        var res = await SendAuthorizedPostAsync("/api/connections", new SendConnectionRequest { TargetUserId = userId });
        if (res.IsSuccessStatusCode)
        {
            SuccessMessage = _loc.IsArabic ? "تم إرسال طلب التواصل بنجاح!" : "Connection request sent successfully!";
        }
        else
        {
            ErrorMessage = _loc.IsArabic ? "تعذر إرسال طلب التواصل." : "Failed to send connection request.";
        }
        return RedirectToPage(new { userId });
    }

    public async Task<IActionResult> OnPostBlockAsync(Guid userId)
    {
        var res = await SendAuthorizedPostAsync("/api/connections/block", new BlockUserRequest { TargetUserId = userId });
        if (res.IsSuccessStatusCode)
        {
            SuccessMessage = _loc.IsArabic ? "تم حظر المستخدم." : "User blocked.";
        }
        return RedirectToPage(new { userId });
    }

    public async Task<IActionResult> OnPostUnblockAsync(Guid userId)
    {
        var res = await SendAuthorizedPostAsync("/api/connections/unblock", new BlockUserRequest { TargetUserId = userId });
        if (res.IsSuccessStatusCode)
        {
            SuccessMessage = _loc.IsArabic ? "تم إلغاء الحظر." : "User unblocked.";
        }
        return RedirectToPage(new { userId });
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
}

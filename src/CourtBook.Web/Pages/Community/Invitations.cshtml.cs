using System.Net.Http.Json;
using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Community;

public class InvitationsModel : PageModel
{
    private readonly ApiClient _api;
    private readonly ITextLocalizer _loc;

    public InvitationsModel(ApiClient api, ITextLocalizer loc)
    {
        _api = api;
        _loc = loc;
    }

    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "received";

    public PagedResult<GameInvitationDto>? ReceivedInvitations { get; set; }
    public PagedResult<GameInvitationDto>? SentInvitations { get; set; }

    [TempData] public string? SuccessMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var token = HttpContext.Session.GetString("JwtToken");
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Login", new { returnUrl = "/Community/Invitations" });
        }

        if (Tab == "sent")
        {
            var res = await SendAuthorizedGetAsync("/api/invitations/sent");
            if (res.IsSuccessStatusCode)
            {
                SentInvitations = await res.Content.ReadFromJsonAsync<PagedResult<GameInvitationDto>>();
            }
        }
        else
        {
            var res = await SendAuthorizedGetAsync("/api/invitations/received");
            if (res.IsSuccessStatusCode)
            {
                ReceivedInvitations = await res.Content.ReadFromJsonAsync<PagedResult<GameInvitationDto>>();
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAcceptAsync(Guid invitationId)
    {
        var res = await SendAuthorizedPostAsync($"/api/invitations/{invitationId}/accept", null);
        if (res.IsSuccessStatusCode)
        {
            var game = await res.Content.ReadFromJsonAsync<GameResponse>();
            SuccessMessage = _loc.IsArabic ? "تم قبول الدعوة والانضمام للمباراة بنجاح!" : "Invitation accepted! You have joined the match.";
            if (game != null)
            {
                return RedirectToPage("/Games/Lobby", new { gameId = game.Id });
            }
        }
        else
        {
            var prob = await res.Content.ReadFromJsonAsync<ProblemDetails>();
            ErrorMessage = prob?.Detail ?? (_loc.IsArabic ? "تعذر قبول الدعوة." : "Failed to accept invitation.");
        }
        return RedirectToPage(new { Tab = "received" });
    }

    public async Task<IActionResult> OnPostDeclineAsync(Guid invitationId)
    {
        var res = await SendAuthorizedPostAsync($"/api/invitations/{invitationId}/decline", null);
        if (res.IsSuccessStatusCode)
        {
            SuccessMessage = _loc.IsArabic ? "تم رفض الدعوة." : "Invitation declined.";
        }
        return RedirectToPage(new { Tab = "received" });
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid invitationId)
    {
        var res = await SendAuthorizedDeleteAsync($"/api/invitations/{invitationId}");
        if (res.IsSuccessStatusCode)
        {
            SuccessMessage = _loc.IsArabic ? "تم إلغاء الدعوة." : "Invitation cancelled.";
        }
        return RedirectToPage(new { Tab = "sent" });
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

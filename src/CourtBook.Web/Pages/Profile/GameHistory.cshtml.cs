using System.Net.Http.Json;
using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Profile;

public class GameHistoryModel : PageModel
{
    private readonly ApiClient _api;
    private readonly ITextLocalizer _loc;

    public GameHistoryModel(ApiClient api, ITextLocalizer loc)
    {
        _api = api;
        _loc = loc;
    }

    [BindProperty(SupportsGet = true)] public string? Sport { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;

    public PagedResult<GameHistoryItemDto>? HistoryResult { get; set; }
    public PlayerReputationDto? Reputation { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid? userId)
    {
        var token = HttpContext.Session.GetString("JwtToken");
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Login", new { returnUrl = "/Profile/GameHistory" });
        }

        // Default to caller's profile if userId is not provided
        var targetUserId = userId;
        if (!targetUserId.HasValue)
        {
            var profileRes = await SendAuthorizedGetAsync("/api/profile");
            if (profileRes.IsSuccessStatusCode)
            {
                var profile = await profileRes.Content.ReadFromJsonAsync<UserProfileResponse>();
                targetUserId = profile?.UserId;
            }
        }

        if (!targetUserId.HasValue)
        {
            return RedirectToPage("/Login");
        }

        var historyQuery = $"/api/community/players/{targetUserId.Value}/history?Page={PageNumber}&PageSize=12&SportType={Uri.EscapeDataString(Sport ?? "")}&Status={Uri.EscapeDataString(Status ?? "")}";
        var historyRes = await SendAuthorizedGetAsync(historyQuery);
        if (historyRes.IsSuccessStatusCode)
        {
            HistoryResult = await historyRes.Content.ReadFromJsonAsync<PagedResult<GameHistoryItemDto>>();
        }

        var repRes = await SendAuthorizedGetAsync($"/api/community/players/{targetUserId.Value}/reputation");
        if (repRes.IsSuccessStatusCode)
        {
            Reputation = await repRes.Content.ReadFromJsonAsync<PlayerReputationDto>();
        }

        return Page();
    }

    private async Task<HttpResponseMessage> SendAuthorizedGetAsync(string url)
    {
        var token = HttpContext.Session.GetString("JwtToken");
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(token))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await _api.Client.SendAsync(req);
    }
}

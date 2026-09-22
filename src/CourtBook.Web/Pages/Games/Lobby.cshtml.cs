using System.Net.Http.Json;
using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Games;

public class LobbyModel : PageModel
{
    private readonly ApiClient _api;
    private readonly ITextLocalizer _loc;
    private readonly ILogger<LobbyModel> _logger;

    public LobbyModel(ApiClient api, ITextLocalizer loc, ILogger<LobbyModel> logger)
    {
        _api = api;
        _loc = loc;
        _logger = logger;
    }

    public GameResponse? Game { get; set; }
    public bool IsOrganizer { get; set; }
    public bool IsParticipant { get; set; }
    public bool CurrentPlayerIsReady { get; set; }
    public string? CurrentPlayerTeam { get; set; }
    public string? CurrentUserName { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid gameId)
    {
        var token = HttpContext.Session.GetString("JwtToken");
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Login", new { returnUrl = $"/Games/Lobby?gameId={gameId}" });
        }

        CurrentUserName = HttpContext.Session.GetString("UserName");

        await LoadLobbyAsync(gameId, token);
        if (Game is null)
        {
            return RedirectToPage("/Games");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostToggleReadyAsync(Guid gameId, bool ready)
    {
        var token = HttpContext.Session.GetString("JwtToken");
        if (string.IsNullOrEmpty(token)) return RedirectToPage("/Login");

        try
        {
            var req = new HttpRequestMessage(HttpMethod.Put, $"/api/games/{gameId}/ready")
            {
                Content = JsonContent.Create(new SetPlayerReadyRequest { IsReady = ready })
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var res = await _api.Client.SendAsync(req);

            if (res.IsSuccessStatusCode)
            {
                TempData["SuccessMessage"] = ready
                    ? (_loc.IsArabic ? "تم تعيين حالتك إلى جاهز!" : "You are marked as Ready!")
                    : (_loc.IsArabic ? "تم إلغاء حالة الجاهزية." : "You are marked as Not Ready.");
            }
            else
            {
                ErrorMessage = "Could not update ready state.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting ready state for game {GameId}", gameId);
            ErrorMessage = "An unexpected error occurred.";
        }

        return RedirectToPage(new { gameId });
    }

    public async Task<IActionResult> OnPostBalanceTeamsAsync(Guid gameId)
    {
        var token = HttpContext.Session.GetString("JwtToken");
        if (string.IsNullOrEmpty(token)) return RedirectToPage("/Login");

        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/games/{gameId}/balance-teams");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var res = await _api.Client.SendAsync(req);

            if (res.IsSuccessStatusCode)
            {
                TempData["SuccessMessage"] = _loc.IsArabic
                    ? "تم توزيع وموازنة الفرق بنجاح باستخدام تصنيف المهارات!"
                    : "Teams balanced successfully using player skill ratings!";
            }
            else
            {
                var err = await res.Content.ReadAsStringAsync();
                ErrorMessage = !string.IsNullOrWhiteSpace(err) ? err.Trim('"') : "Failed to balance teams.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error balancing teams for game {GameId}", gameId);
            ErrorMessage = "An unexpected error occurred.";
        }

        return RedirectToPage(new { gameId });
    }

    private async Task LoadLobbyAsync(Guid gameId, string token)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"/api/games/{gameId}/lobby");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var res = await _api.Client.SendAsync(req);

            if (res.IsSuccessStatusCode)
            {
                Game = await res.Content.ReadFromJsonAsync<GameResponse>();
                if (Game != null)
                {
                    var current = Game.Participants.FirstOrDefault(p =>
                        p.UserName.Equals(CurrentUserName, StringComparison.OrdinalIgnoreCase));

                    IsOrganizer = Game.CreatorName.Equals(CurrentUserName, StringComparison.OrdinalIgnoreCase);
                    IsParticipant = current != null;
                    CurrentPlayerIsReady = current?.IsReady ?? false;
                    CurrentPlayerTeam = current?.Team;
                }
            }
            else
            {
                var err = await res.Content.ReadAsStringAsync();
                ErrorMessage = !string.IsNullOrWhiteSpace(err) ? err.Trim('"') : "Could not load game lobby.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load lobby for {GameId}", gameId);
            ErrorMessage = "Failed to communicate with lobby service.";
        }
    }
}

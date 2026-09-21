using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Games;

public class IndexModel : PageModel
{
    private readonly ApiClient _api;
    private readonly ITextLocalizer _loc;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(ApiClient api, ITextLocalizer loc, ILogger<IndexModel> logger)
    {
        _api = api;
        _loc = loc;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)] public string? Sport { get; set; }
    [BindProperty(SupportsGet = true)] public string? City { get; set; }
    [BindProperty(SupportsGet = true)] public string? AgeGroup { get; set; }
    [BindProperty(SupportsGet = true)] public string? SkillLevel { get; set; }

    public PagedResult<GameResponse>? GamesResult { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadGamesAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostJoinAsync(Guid gameId)
    {
        var token = HttpContext.Session.GetString("JwtToken");
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Login", new { returnUrl = "/Games" });
        }

        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/games/{gameId}/join");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var res = await _api.Client.SendAsync(req);

            if (res.IsSuccessStatusCode)
            {
                TempData["SuccessMessage"] = _loc.IsArabic 
                    ? "تم انضمامك إلى المباراة بنجاح! حظاً موفقاً." 
                    : "You have joined the match successfully! Have a great game.";
                return RedirectToPage("/Games", new { sport = Sport, city = City, ageGroup = AgeGroup });
            }

            var problem = await res.Content.ReadAsStringAsync();
            if (problem.Contains("eligibility requirements"))
            {
                ErrorMessage = _loc.IsArabic
                    ? "لا يمكنك الانضمام لهذه المباراة لأن عمرك لا يطابق الشروط العمرية المحددة للمباراة."
                    : "You can't join this game because your age does not meet the game's eligibility requirements.";
            }
            else if (problem.Contains("date of birth"))
            {
                ErrorMessage = _loc.IsArabic
                    ? "يرجى تحديث تاريخ ميلادك في الملف الشخصي لتتمكن من الانضمام للمباريات المحددة عمرياً."
                    : "Please update your profile with your date of birth to join age-restricted community games.";
            }
            else
            {
                ErrorMessage = !string.IsNullOrWhiteSpace(problem) ? problem.Trim('"') : "Failed to join game.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining game {GameId}", gameId);
            ErrorMessage = "An unexpected error occurred. Please try again.";
        }

        await LoadGamesAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostLeaveAsync(Guid gameId)
    {
        var token = HttpContext.Session.GetString("JwtToken");
        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Login", new { returnUrl = "/Games" });
        }

        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/games/{gameId}/leave");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var res = await _api.Client.SendAsync(req);

            if (res.IsSuccessStatusCode)
            {
                TempData["SuccessMessage"] = _loc.IsArabic ? "تم إلغاء انضمامك من المباراة." : "You have left the match.";
                return RedirectToPage("/Games", new { sport = Sport, city = City, ageGroup = AgeGroup });
            }

            var err = await res.Content.ReadAsStringAsync();
            ErrorMessage = !string.IsNullOrWhiteSpace(err) ? err.Trim('"') : "Could not leave game.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving game {GameId}", gameId);
            ErrorMessage = "An unexpected error occurred. Please try again.";
        }

        await LoadGamesAsync();
        return Page();
    }

    private async Task LoadGamesAsync()
    {
        var q = new List<string> { "page=1", "pageSize=30" };
        if (!string.IsNullOrWhiteSpace(Sport)) q.Add($"sport={Uri.EscapeDataString(Sport)}");
        if (!string.IsNullOrWhiteSpace(City)) q.Add($"city={Uri.EscapeDataString(City)}");
        if (!string.IsNullOrWhiteSpace(AgeGroup)) q.Add($"ageGroup={Uri.EscapeDataString(AgeGroup)}");
        if (!string.IsNullOrWhiteSpace(SkillLevel)) q.Add($"skillLevel={Uri.EscapeDataString(SkillLevel)}");

        var queryString = string.Join("&", q);
        try
        {
            var res = await _api.Client.GetAsync($"/api/games?{queryString}");
            if (res.IsSuccessStatusCode)
            {
                GamesResult = await res.Content.ReadFromJsonAsync<PagedResult<GameResponse>>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load games query");
            GamesResult = PagedResult<GameResponse>.Empty(1, 30);
        }
    }
}

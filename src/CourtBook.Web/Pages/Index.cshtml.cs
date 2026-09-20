using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages;

public class IndexModel : PageModel
{
    private readonly ApiClient _api;

    public IndexModel(ApiClient api)
    {
        _api = api;
    }

    public IReadOnlyList<VenueCardDto> FeaturedVenues { get; set; } = [];
    public IReadOnlyList<GameResponse> UpcomingGames { get; set; } = [];
    public bool ApiError { get; set; } = false;

    public async Task OnGetAsync()
    {
        try
        {
            // Fetch top rated venues
            var venuesResult = await _api.Client.GetFromJsonAsync<PagedResult<VenueCardDto>>(
                "/api/venues/search?sortBy=rating_desc&pageSize=6");
            
            if (venuesResult?.Items != null)
            {
                FeaturedVenues = venuesResult.Items;
            }

            // Fetch upcoming open community matches
            var gamesResult = await _api.Client.GetFromJsonAsync<PagedResult<GameResponse>>(
                "/api/games?pageSize=3");

            if (gamesResult?.Items != null)
            {
                UpcomingGames = gamesResult.Items;
            }
        }
        catch
        {
            ApiError = true;
        }
    }
}

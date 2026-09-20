using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Venues;

public class IndexModel : PageModel
{
    private readonly ApiClient _api;

    public IndexModel(ApiClient api)
    {
        _api = api;
    }

    [BindProperty(SupportsGet = true)]
    public string? Sport { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? City { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Area { get; set; }

    [BindProperty(SupportsGet = true)]
    public decimal? MinPrice { get; set; }

    [BindProperty(SupportsGet = true)]
    public decimal? MaxPrice { get; set; }

    [BindProperty(SupportsGet = true)]
    public double? MinRating { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string SortBy { get; set; } = "rating_desc";

    [BindProperty(SupportsGet = true)]
    public int CurrentPage { get; set; } = 1;

    public PagedResult<VenueCardDto> SearchResult { get; set; } = PagedResult<VenueCardDto>.Empty(1, 9);
    public bool ApiError { get; set; } = false;

    public async Task OnGetAsync()
    {
        try
        {
            var queryParams = new List<string>
            {
                $"page={Math.Max(1, CurrentPage)}",
                "pageSize=9",
                $"sortBy={Uri.EscapeDataString(SortBy ?? "rating_desc")}"
            };

            if (!string.IsNullOrWhiteSpace(Sport))
                queryParams.Add($"sport={Uri.EscapeDataString(Sport)}");

            if (!string.IsNullOrWhiteSpace(City))
                queryParams.Add($"city={Uri.EscapeDataString(City)}");

            if (!string.IsNullOrWhiteSpace(Area))
                queryParams.Add($"area={Uri.EscapeDataString(Area)}");

            if (!string.IsNullOrWhiteSpace(Search))
                queryParams.Add($"search={Uri.EscapeDataString(Search)}");

            if (MinPrice.HasValue)
                queryParams.Add($"minPrice={MinPrice.Value}");

            if (MaxPrice.HasValue)
                queryParams.Add($"maxPrice={MaxPrice.Value}");

            if (MinRating.HasValue)
                queryParams.Add($"minRating={MinRating.Value}");

            var queryString = string.Join("&", queryParams);
            var endpoint = $"/api/venues/search?{queryString}";

            var result = await _api.Client.GetFromJsonAsync<PagedResult<VenueCardDto>>(endpoint);
            if (result != null)
            {
                SearchResult = result;
            }
        }
        catch
        {
            ApiError = true;
        }
    }
}

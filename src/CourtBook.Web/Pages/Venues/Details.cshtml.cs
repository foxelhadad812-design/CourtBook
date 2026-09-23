using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages.Venues;

public class DetailsModel : PageModel
{
    private readonly ApiClient _api;
    private readonly ILogger<DetailsModel> _logger;

    public DetailsModel(ApiClient api, ILogger<DetailsModel> logger)
    {
        _api = api;
        _logger = logger;
    }

    public VenueResponse? Venue { get; set; }
    public PagedResult<ReviewResponse>? Reviews { get; set; }
    public bool IsNotFound { get; set; } = false;

    [BindProperty(SupportsGet = true)] public string? SortBy { get; set; } = "recent";
    [BindProperty(SupportsGet = true)] public int? FilterRating { get; set; }
    public VenueRatingSummaryDto? RatingSummary { get; set; }

    // Computed review metrics
    public Dictionary<int, int> RatingDistribution { get; set; } = new()
    {
        { 5, 0 }, { 4, 0 }, { 3, 0 }, { 2, 0 }, { 1, 0 }
    };
    public Dictionary<int, int> RatingPercentages { get; set; } = new()
    {
        { 5, 0 }, { 4, 0 }, { 3, 0 }, { 2, 0 }, { 1, 0 }
    };

    public double CourtQualityAvg { get; set; } = 0;
    public double CleanlinessAvg { get; set; } = 0;
    public double StaffAvg { get; set; } = 0;
    public double ValueAvg { get; set; } = 0;

    public async Task<IActionResult> OnGetAsync(Guid? id, [FromQuery] Guid? venueId)
    {
        var token = HttpContext.Session.GetString("JwtToken");
        if (string.IsNullOrEmpty(token))
        {
            var returnUrl = Uri.EscapeDataString(Request.Path + Request.QueryString);
            return Redirect($"/Login?returnUrl={returnUrl}");
        }

        var targetId = id ?? venueId;
        if (!targetId.HasValue || targetId.Value == Guid.Empty)
        {
            if (Request.Query.TryGetValue("id", out var queryIdVal) && Guid.TryParse(queryIdVal, out var parsedQueryId))
            {
                targetId = parsedQueryId;
            }
            else
            {
                IsNotFound = true;
                return Page();
            }
        }

        try
        {
            // 1. Fetch Venue Details from API
            var venueResp = await _api.Client.GetAsync($"/api/venues/{targetId.Value}");
            if (venueResp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                var returnUrl = Uri.EscapeDataString(Request.Path + Request.QueryString);
                return Redirect($"/Login?returnUrl={returnUrl}");
            }

            if (!venueResp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch venue {VenueId}. Status: {StatusCode}", targetId.Value, venueResp.StatusCode);
                IsNotFound = true;
                return Page();
            }

            Venue = await venueResp.Content.ReadFromJsonAsync<VenueResponse>(ApiClient.JsonOptions);
            if (Venue == null)
            {
                _logger.LogWarning("Venue {VenueId} deserialized to null", targetId.Value);
                IsNotFound = true;
                return Page();
            }

            // 2. Fetch Structured Rating Summary from API
            try
            {
                var summaryResp = await _api.Client.GetAsync($"/api/venues/{targetId.Value}/reviews/summary");
                if (summaryResp.IsSuccessStatusCode)
                {
                    RatingSummary = await summaryResp.Content.ReadFromJsonAsync<VenueRatingSummaryDto>(ApiClient.JsonOptions);
                    if (RatingSummary != null)
                    {
                        RatingDistribution[5] = RatingSummary.FiveStarCount;
                        RatingDistribution[4] = RatingSummary.FourStarCount;
                        RatingDistribution[3] = RatingSummary.ThreeStarCount;
                        RatingDistribution[2] = RatingSummary.TwoStarCount;
                        RatingDistribution[1] = RatingSummary.OneStarCount;

                        RatingPercentages[5] = (int)Math.Round(RatingSummary.FiveStarPercent);
                        RatingPercentages[4] = (int)Math.Round(RatingSummary.FourStarPercent);
                        RatingPercentages[3] = (int)Math.Round(RatingSummary.ThreeStarPercent);
                        RatingPercentages[2] = (int)Math.Round(RatingSummary.TwoStarPercent);
                        RatingPercentages[1] = (int)Math.Round(RatingSummary.OneStarPercent);

                        CourtQualityAvg = RatingSummary.CourtQualityAverage;
                        CleanlinessAvg = RatingSummary.CleanlinessAverage;
                        StaffAvg = RatingSummary.StaffAverage;
                        ValueAvg = RatingSummary.ValueAverage;
                    }
                }
            }
            catch (Exception sx)
            {
                _logger.LogInformation("Rating summary fetch fallback: {Message}", sx.Message);
            }

            // 3. Fetch Verified Reviews from API (with sorting and star filtering)
            try
            {
                var queryParams = $"page=1&pageSize=50&sortBy={Uri.EscapeDataString(SortBy ?? "recent")}";
                if (FilterRating.HasValue && FilterRating.Value >= 1 && FilterRating.Value <= 5)
                {
                    queryParams += $"&rating={FilterRating.Value}";
                }

                var reviewsResp = await _api.Client.GetAsync($"/api/venues/{targetId.Value}/reviews?{queryParams}");
                if (reviewsResp.IsSuccessStatusCode)
                {
                    Reviews = await reviewsResp.Content.ReadFromJsonAsync<PagedResult<ReviewResponse>>(ApiClient.JsonOptions);
                    if (RatingSummary == null)
                    {
                        CalculateReviewMetrics();
                    }
                }
            }
            catch (Exception rx)
            {
                _logger.LogInformation("Review fetch fallback: {Message}", rx.Message);
                Reviews = PagedResult<ReviewResponse>.Empty(1, 20);
            }

            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while loading venue details for {VenueId}", targetId.Value);
            IsNotFound = true;
            return Page();
        }
    }

    private void CalculateReviewMetrics()
    {
        if (Reviews == null || !Reviews.Items.Any()) return;

        var items = Reviews.Items;
        var total = items.Count;

        foreach (var r in items)
        {
            int rating = Math.Clamp(r.OverallRating, 1, 5);
            RatingDistribution[rating]++;
        }

        foreach (var star in RatingDistribution.Keys)
        {
            RatingPercentages[star] = (int)Math.Round((double)RatingDistribution[star] / total * 100);
        }

        CourtQualityAvg = Math.Round(items.Average(r => r.CourtQualityRating), 1);
        CleanlinessAvg = Math.Round(items.Average(r => r.CleanlinessRating), 1);
        StaffAvg = Math.Round(items.Average(r => r.StaffRating), 1);
        ValueAvg = Math.Round(items.Average(r => r.ValueRating), 1);
    }
}

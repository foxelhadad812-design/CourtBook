using System.Text.RegularExpressions;

namespace CourtBook.Web.Helpers;

public static class CourtImageHelper
{
    private static readonly string[] FootballImages =
    [
        "/images/venues/football/football-pitch-01.jpg",
        "/images/venues/football/football-pitch-02.jpg",
        "/images/venues/football/football-pitch-03.jpg",
        "/images/venues/football/football-pitch-04.jpg",
        "/images/venues/football/football-pitch-05.jpg",
        "/images/venues/football/football-pitch-06.jpg"
    ];

    private static readonly string[] PadelImages =
    [
        "/images/venues/padel/padel-court-01.jpg",
        "/images/venues/padel/padel-court-02.jpg",
        "/images/venues/padel/padel-court-03.jpg",
        "/images/venues/padel/padel-court-04.jpg",
        "/images/venues/padel/padel-court-05.jpg"
    ];

    private static readonly string[] TennisClayImages =
    [
        "/images/venues/tennis/tennis-clay-01.jpg",
        "/images/venues/tennis/tennis-clay-02.jpg"
    ];

    private static readonly string[] TennisHardImages =
    [
        "/images/venues/tennis/tennis-hard-01.jpg",
        "/images/venues/tennis/tennis-hard-02.jpg",
        "/images/venues/tennis/tennis-detail-01.jpg"
    ];

    private static readonly string[] BasketballImages =
    [
        "/images/venues/basketball/basketball-court-01.jpg",
        "/images/venues/basketball/basketball-indoor-01.jpg",
        "/images/venues/basketball/basketball-indoor-02.jpg"
    ];

    private static readonly string[] VolleyballImages =
    [
        "/images/venues/volleyball/volleyball-beach-01.jpg",
        "/images/venues/volleyball/volleyball-indoor-01.jpg"
    ];

    private static readonly string[] BadmintonImages =
    [
        "/images/venues/badminton/badminton-court-01.jpg",
        "/images/venues/badminton/badminton-court-02.jpg",
        "/images/venues/badminton/badminton-arena-01.jpg"
    ];

    public static string GetCourtImageUrl(string courtName, string sportType, bool isIndoor = false, string? surfaceType = null, int capacity = 10, int fallbackIndex = 0)
    {
        var sport = sportType.Trim().ToLowerInvariant();
        var numMatch = Regex.Match(courtName, @"\d+");
        int index = numMatch.Success ? int.Parse(numMatch.Value) - 1 : fallbackIndex;
        if (index < 0) index = Math.Abs(courtName.GetHashCode()) % 6;

        return sport switch
        {
            "football" => FootballImages[Math.Abs(index) % FootballImages.Length],
            "padel" => isIndoor && index % 2 == 1 
                ? "/images/venues/padel/padel-indoor-01.jpg" 
                : PadelImages[Math.Abs(index) % PadelImages.Length],
            "tennis" => (surfaceType?.ToLowerInvariant().Contains("hard") == true)
                ? TennisHardImages[Math.Abs(index) % TennisHardImages.Length]
                : TennisClayImages[Math.Abs(index) % TennisClayImages.Length],
            "basketball" => BasketballImages[Math.Abs(index) % BasketballImages.Length],
            "volleyball" => isIndoor 
                ? "/images/venues/volleyball/volleyball-indoor-01.jpg" 
                : VolleyballImages[Math.Abs(index) % VolleyballImages.Length],
            "badminton" => BadmintonImages[Math.Abs(index) % BadmintonImages.Length],
            _ => "/images/venues/fallbacks/venue-fallback.jpg"
        };
    }

    public static string GetFallbackImageUrl(string sportType)
    {
        var sport = sportType.Trim().ToLowerInvariant();
        return $"/images/venues/fallbacks/{sport}-fallback.jpg";
    }
}

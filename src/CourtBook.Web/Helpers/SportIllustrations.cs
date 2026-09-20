namespace CourtBook.Web.Helpers;

public static class SportIllustrations
{
    public static string GetSvgForSport(string sportType)
    {
        var imgUrl = sportType?.ToLower() switch
        {
            "football" => "/images/football.jpg",
            "padel" => "/images/padel.jpg",
            "tennis" => "/images/tennis.jpg",
            "basketball" => "/images/basketball.jpg",
            _ => "/images/venue.jpg"
        };
        return $@"<img src=""{imgUrl}"" class=""w-100 h-100 object-fit-cover"" alt=""{sportType} court"">";
    }

    public static string GetVenueHeroSvg() 
    {
        return @"<img src=""/images/venue.jpg"" class=""w-100 h-100 object-fit-cover"" alt=""Venue image"">";
    }
}

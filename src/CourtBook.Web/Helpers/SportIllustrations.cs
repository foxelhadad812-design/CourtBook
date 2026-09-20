namespace CourtBook.Web.Helpers;

public static class SportIllustrations
{
    public static string GetSvgForSport(string sportType)
    {
        return sportType?.ToLower() switch
        {
            "football" => FootballSvg,
            "padel" => PadelSvg,
            "tennis" => TennisSvg,
            "basketball" => BasketballSvg,
            _ => DefaultVenueSvg
        };
    }

    public static string GetVenueHeroSvg() => DefaultVenueSvg;

    private const string FootballSvg = @"
<svg viewBox=""0 0 400 250"" xmlns=""http://www.w3.org/2000/svg"" class=""w-100 h-100 object-fit-cover"">
  <rect width=""400"" height=""250"" fill=""#2d6a4f""/>
  <!-- Pitch pattern (stripes) -->
  <rect x=""0"" y=""0"" width=""40"" height=""250"" fill=""rgba(0,0,0,0.05)""/>
  <rect x=""80"" y=""0"" width=""40"" height=""250"" fill=""rgba(0,0,0,0.05)""/>
  <rect x=""160"" y=""0"" width=""40"" height=""250"" fill=""rgba(0,0,0,0.05)""/>
  <rect x=""240"" y=""0"" width=""40"" height=""250"" fill=""rgba(0,0,0,0.05)""/>
  <rect x=""320"" y=""0"" width=""40"" height=""250"" fill=""rgba(0,0,0,0.05)""/>
  
  <rect x=""20"" y=""20"" width=""360"" height=""210"" fill=""none"" stroke=""rgba(255,255,255,0.7)"" stroke-width=""4""/>
  <!-- Center line -->
  <line x1=""200"" y1=""20"" x2=""200"" y2=""230"" stroke=""rgba(255,255,255,0.7)"" stroke-width=""4""/>
  <!-- Center circle -->
  <circle cx=""200"" cy=""125"" r=""40"" fill=""none"" stroke=""rgba(255,255,255,0.7)"" stroke-width=""4""/>
  <circle cx=""200"" cy=""125"" r=""4"" fill=""rgba(255,255,255,0.7)""/>
  <!-- Penalty areas -->
  <rect x=""20"" y=""65"" width=""60"" height=""120"" fill=""none"" stroke=""rgba(255,255,255,0.7)"" stroke-width=""4""/>
  <rect x=""320"" y=""65"" width=""60"" height=""120"" fill=""none"" stroke=""rgba(255,255,255,0.7)"" stroke-width=""4""/>
  <!-- Goal areas -->
  <rect x=""20"" y=""95"" width=""20"" height=""60"" fill=""none"" stroke=""rgba(255,255,255,0.7)"" stroke-width=""4""/>
  <rect x=""360"" y=""95"" width=""20"" height=""60"" fill=""none"" stroke=""rgba(255,255,255,0.7)"" stroke-width=""4""/>
  <!-- Corner arcs -->
  <path d=""M 40 20 A 20 20 0 0 0 20 40"" fill=""none"" stroke=""rgba(255,255,255,0.7)"" stroke-width=""4""/>
  <path d=""M 20 210 A 20 20 0 0 0 40 230"" fill=""none"" stroke=""rgba(255,255,255,0.7)"" stroke-width=""4""/>
  <path d=""M 380 20 A 20 20 0 0 1 360 40"" fill=""none"" stroke=""rgba(255,255,255,0.7)"" stroke-width=""4""/>
  <path d=""M 380 230 A 20 20 0 0 0 360 210"" fill=""none"" stroke=""rgba(255,255,255,0.7)"" stroke-width=""4""/>
  
  <text x=""380"" y=""235"" font-family=""sans-serif"" font-size=""14"" font-weight=""bold"" fill=""rgba(255,255,255,0.3)"" text-anchor=""end"">FOOTBALL</text>
</svg>";

    private const string PadelSvg = @"
<svg viewBox=""0 0 400 250"" xmlns=""http://www.w3.org/2000/svg"" class=""w-100 h-100 object-fit-cover"">
  <rect width=""400"" height=""250"" fill=""#0077b6""/>
  <!-- Glass walls shadow/border -->
  <rect x=""40"" y=""30"" width=""320"" height=""190"" fill=""rgba(255,255,255,0.05)""/>
  <rect x=""40"" y=""30"" width=""320"" height=""190"" fill=""none"" stroke=""rgba(173,216,230,0.6)"" stroke-width=""10""/>
  <!-- Court bounds -->
  <rect x=""60"" y=""50"" width=""280"" height=""150"" fill=""none"" stroke=""rgba(255,255,255,0.9)"" stroke-width=""4""/>
  <!-- Net -->
  <line x1=""200"" y1=""40"" x2=""200"" y2=""210"" stroke=""#f8f9fa"" stroke-width=""6""/>
  <line x1=""200"" y1=""40"" x2=""200"" y2=""210"" stroke=""#343a40"" stroke-width=""2"" stroke-dasharray=""4,4""/>
  <!-- Service lines -->
  <line x1=""120"" y1=""50"" x2=""120"" y2=""200"" stroke=""rgba(255,255,255,0.9)"" stroke-width=""4""/>
  <line x1=""280"" y1=""50"" x2=""280"" y2=""200"" stroke=""rgba(255,255,255,0.9)"" stroke-width=""4""/>
  <!-- Center service line -->
  <line x1=""120"" y1=""125"" x2=""280"" y2=""125"" stroke=""rgba(255,255,255,0.9)"" stroke-width=""4""/>
  <text x=""375"" y=""235"" font-family=""sans-serif"" font-size=""14"" font-weight=""bold"" fill=""rgba(255,255,255,0.4)"" text-anchor=""end"">PADEL</text>
</svg>";

    private const string TennisSvg = @"
<svg viewBox=""0 0 400 250"" xmlns=""http://www.w3.org/2000/svg"" class=""w-100 h-100 object-fit-cover"">
  <!-- Outer surface (darker clay or green) -->
  <rect width=""400"" height=""250"" fill=""#297345""/>
  <!-- Inner court (Clay color) -->
  <rect x=""50"" y=""40"" width=""300"" height=""170"" fill=""#c35d3d""/>
  <!-- Doubles bounds -->
  <rect x=""50"" y=""40"" width=""300"" height=""170"" fill=""none"" stroke=""rgba(255,255,255,0.9)"" stroke-width=""3""/>
  <!-- Singles bounds -->
  <rect x=""50"" y=""65"" width=""300"" height=""120"" fill=""none"" stroke=""rgba(255,255,255,0.9)"" stroke-width=""3""/>
  <!-- Net -->
  <line x1=""200"" y1=""35"" x2=""200"" y2=""215"" stroke=""#f8f9fa"" stroke-width=""5""/>
  <line x1=""200"" y1=""35"" x2=""200"" y2=""215"" stroke=""#495057"" stroke-width=""2"" stroke-dasharray=""3,3""/>
  <!-- Service lines -->
  <line x1=""125"" y1=""65"" x2=""125"" y2=""185"" stroke=""rgba(255,255,255,0.9)"" stroke-width=""3""/>
  <line x1=""275"" y1=""65"" x2=""275"" y2=""185"" stroke=""rgba(255,255,255,0.9)"" stroke-width=""3""/>
  <!-- Center service line -->
  <line x1=""125"" y1=""125"" x2=""275"" y2=""125"" stroke=""rgba(255,255,255,0.9)"" stroke-width=""3""/>
  <!-- Center marks -->
  <line x1=""50"" y1=""125"" x2=""60"" y2=""125"" stroke=""rgba(255,255,255,0.9)"" stroke-width=""3""/>
  <line x1=""340"" y1=""125"" x2=""350"" y2=""125"" stroke=""rgba(255,255,255,0.9)"" stroke-width=""3""/>
  <text x=""380"" y=""235"" font-family=""sans-serif"" font-size=""14"" font-weight=""bold"" fill=""rgba(255,255,255,0.4)"" text-anchor=""end"">TENNIS</text>
</svg>";

    private const string BasketballSvg = @"
<svg viewBox=""0 0 400 250"" xmlns=""http://www.w3.org/2000/svg"" class=""w-100 h-100 object-fit-cover"">
  <rect width=""400"" height=""250"" fill=""#d4a373""/>
  <!-- Wood planks -->
  <line x1=""0"" y1=""20"" x2=""400"" y2=""20"" stroke=""rgba(0,0,0,0.05)"" stroke-width=""1""/>
  <line x1=""0"" y1=""40"" x2=""400"" y2=""40"" stroke=""rgba(0,0,0,0.05)"" stroke-width=""1""/>
  <line x1=""0"" y1=""60"" x2=""400"" y2=""60"" stroke=""rgba(0,0,0,0.05)"" stroke-width=""1""/>
  <line x1=""0"" y1=""80"" x2=""400"" y2=""80"" stroke=""rgba(0,0,0,0.05)"" stroke-width=""1""/>
  <line x1=""0"" y1=""100"" x2=""400"" y2=""100"" stroke=""rgba(0,0,0,0.05)"" stroke-width=""1""/>
  <line x1=""0"" y1=""120"" x2=""400"" y2=""120"" stroke=""rgba(0,0,0,0.05)"" stroke-width=""1""/>
  <line x1=""0"" y1=""140"" x2=""400"" y2=""140"" stroke=""rgba(0,0,0,0.05)"" stroke-width=""1""/>
  <line x1=""0"" y1=""160"" x2=""400"" y2=""160"" stroke=""rgba(0,0,0,0.05)"" stroke-width=""1""/>
  <line x1=""0"" y1=""180"" x2=""400"" y2=""180"" stroke=""rgba(0,0,0,0.05)"" stroke-width=""1""/>
  <line x1=""0"" y1=""200"" x2=""400"" y2=""200"" stroke=""rgba(0,0,0,0.05)"" stroke-width=""1""/>
  <line x1=""0"" y1=""220"" x2=""400"" y2=""220"" stroke=""rgba(0,0,0,0.05)"" stroke-width=""1""/>
  
  <!-- Court border -->
  <rect x=""20"" y=""15"" width=""360"" height=""220"" fill=""none"" stroke=""#212529"" stroke-width=""4""/>
  <!-- Center line -->
  <line x1=""200"" y1=""15"" x2=""200"" y2=""235"" stroke=""#212529"" stroke-width=""4""/>
  <!-- Center circle -->
  <circle cx=""200"" cy=""125"" r=""30"" fill=""#e76f51"" stroke=""#212529"" stroke-width=""4""/>
  <circle cx=""200"" cy=""125"" r=""10"" fill=""none"" stroke=""#212529"" stroke-width=""4""/>
  <!-- Left key -->
  <rect x=""20"" y=""85"" width=""70"" height=""80"" fill=""#e76f51"" stroke=""#212529"" stroke-width=""4""/>
  <path d=""M 90 85 A 40 40 0 0 1 90 165"" fill=""none"" stroke=""#212529"" stroke-width=""4""/>
  <path d=""M 90 165 A 40 40 0 0 1 90 85"" fill=""none"" stroke=""#212529"" stroke-width=""4"" stroke-dasharray=""4,4""/>
  <!-- Left 3pt arc -->
  <path d=""M 20 30 L 40 30 A 110 110 0 0 1 40 220 L 20 220"" fill=""none"" stroke=""#212529"" stroke-width=""4""/>
  <!-- Right key -->
  <rect x=""310"" y=""85"" width=""70"" height=""80"" fill=""#e76f51"" stroke=""#212529"" stroke-width=""4""/>
  <path d=""M 310 165 A 40 40 0 0 1 310 85"" fill=""none"" stroke=""#212529"" stroke-width=""4""/>
  <path d=""M 310 85 A 40 40 0 0 1 310 165"" fill=""none"" stroke=""#212529"" stroke-width=""4"" stroke-dasharray=""4,4""/>
  <!-- Right 3pt arc -->
  <path d=""M 380 30 L 360 30 A 110 110 0 0 0 360 220 L 380 220"" fill=""none"" stroke=""#212529"" stroke-width=""4""/>
  <!-- Backboards and hoops -->
  <line x1=""35"" y1=""105"" x2=""35"" y2=""145"" stroke=""#fff"" stroke-width=""4""/>
  <circle cx=""42"" cy=""125"" r=""5"" fill=""none"" stroke=""#e63946"" stroke-width=""3""/>
  <line x1=""365"" y1=""105"" x2=""365"" y2=""145"" stroke=""#fff"" stroke-width=""4""/>
  <circle cx=""358"" cy=""125"" r=""5"" fill=""none"" stroke=""#e63946"" stroke-width=""3""/>
  
  <text x=""375"" y=""230"" font-family=""sans-serif"" font-size=""14"" font-weight=""bold"" fill=""rgba(0,0,0,0.3)"" text-anchor=""end"">BASKETBALL</text>
</svg>";

    private const string DefaultVenueSvg = @"
<svg viewBox=""0 0 400 250"" xmlns=""http://www.w3.org/2000/svg"" class=""w-100 h-100 object-fit-cover"">
  <rect width=""400"" height=""250"" fill=""#1a7a4a""/>
  <!-- Abstract geometric sports shapes -->
  <circle cx=""100"" cy=""100"" r=""150"" fill=""rgba(255,255,255,0.05)"" />
  <circle cx=""350"" cy=""200"" r=""80"" fill=""rgba(255,255,255,0.05)"" />
  <path d=""M 0 250 L 400 50 L 400 250 Z"" fill=""rgba(0,0,0,0.1)"" />
  <text x=""200"" y=""130"" font-family=""sans-serif"" font-size=""24"" font-weight=""bold"" fill=""rgba(255,255,255,0.8)"" text-anchor=""middle"" letter-spacing=""2"">COURTBOOK</text>
  <text x=""200"" y=""160"" font-family=""sans-serif"" font-size=""14"" fill=""rgba(255,255,255,0.5)"" text-anchor=""middle"">PREMIUM VENUE</text>
</svg>";

}

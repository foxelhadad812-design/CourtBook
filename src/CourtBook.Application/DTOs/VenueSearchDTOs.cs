using CourtBook.Application.Common;

namespace CourtBook.Application.DTOs;

public class VenueSearchRequest : PagedRequest
{
    public string? Sport { get; set; }
    public string? City { get; set; }
    public string? Area { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public double? MinRating { get; set; }
    public DateOnly? Date { get; set; }
    public TimeOnly? AvailableTime { get; set; }
    public List<Guid>? AmenityIds { get; set; }
}

public class VenueCardDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double AverageRating { get; set; }
    public int TotalReviews { get; set; }
    public bool IsVerified { get; set; }
    public decimal StartingPrice { get; set; }
    public List<string> Sports { get; set; } = [];
    public List<string> Amenities { get; set; } = [];
    public string? PrimaryImageUrl { get; set; }
    public int CourtsCount { get; set; }
}

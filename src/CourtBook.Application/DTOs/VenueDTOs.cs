namespace CourtBook.Application.DTOs;

public class CreateVenueRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Website { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public List<Guid> AmenityIds { get; set; } = [];
}

public class UpdateVenueRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Website { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool IsActive { get; set; } = true;
    public List<Guid> AmenityIds { get; set; } = [];
}

public class VenueResponse
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Country { get; set; } = "Egypt";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Website { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsVerified { get; set; } = false;
    public double AverageRating { get; set; } = 0.0;
    public int TotalReviews { get; set; } = 0;
    public DateTime CreatedAt { get; set; }

    public List<CourtResponse> Courts { get; set; } = [];
    public List<VenueAmenityDto> Amenities { get; set; } = [];
    public List<VenueImageDto> Images { get; set; } = [];
    public List<OperatingHourDto> OperatingHours { get; set; } = [];
    public CancellationPolicyDto? CancellationPolicy { get; set; }
    public List<string> Sports { get; set; } = [];
}

public class VenueAmenityDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
}

public class VenueImageDto
{
    public Guid Id { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public int DisplayOrder { get; set; }
    public string? Caption { get; set; }
}

public class OperatingHourDto
{
    public DayOfWeek DayOfWeek { get; set; }
    public string DayName { get; set; } = string.Empty;
    public string OpenTime { get; set; } = string.Empty;
    public string CloseTime { get; set; } = string.Empty;
    public bool IsClosed { get; set; }
}

public class CancellationPolicyDto
{
    public int FreeCancellationHours { get; set; } = 24;
    public decimal LateCancellationFeePercent { get; set; } = 50.0m;
    public string PolicyDescription { get; set; } = string.Empty;
}

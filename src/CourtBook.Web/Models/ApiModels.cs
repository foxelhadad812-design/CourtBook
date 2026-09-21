using CourtBook.Application.DTOs;

namespace CourtBook.Web.Models;

public class LoginRequest { public string Email { get; set; } = ""; public string Password { get; set; } = ""; }
public class AuthResponse { public string Token { get; set; } = ""; public string Role { get; set; } = ""; }
public class RegisterRequest 
{ 
    public string Name { get; set; } = ""; 
    public string Email { get; set; } = ""; 
    public string Password { get; set; } = ""; 
    public string Phone { get; set; } = ""; 
    public string Role { get; set; } = "Client"; 
    public bool AcceptTerms { get; set; } 
    public string? TermsVersion { get; set; } 
}

public class VenueResponse
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string City { get; set; } = "";
    public string Area { get; set; } = "";
    public string Address { get; set; } = "";
    public string Country { get; set; } = "Egypt";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string Phone { get; set; } = "";
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

public class CourtResponse
{
    public Guid Id { get; set; }
    public Guid VenueId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string SportType { get; set; } = "";
    public decimal PricePerHour { get; set; }
    public string SurfaceType { get; set; } = "";
    public bool IsIndoor { get; set; }
    public int Capacity { get; set; }
    public bool IsActive { get; set; }
    public List<ScheduleResponse> Schedules { get; set; } = [];
}
public class ScheduleResponse { public Guid Id { get; set; } public int DayOfWeek { get; set; } public string OpenTime { get; set; } = ""; public string CloseTime { get; set; } = ""; }
public class CreateScheduleRequest { public int DayOfWeek { get; set; } public string OpenTime { get; set; } = ""; public string CloseTime { get; set; } = ""; }

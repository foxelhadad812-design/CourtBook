using System.Text.Json.Serialization;
using CourtBook.Domain.Enums;

namespace CourtBook.Application.DTOs;

public class AdminVenueDto
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string OwnerPhone { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public VenueApprovalStatus ApprovalStatus { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public bool IsActive { get; set; }
    public int TotalCourts { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string> Sports { get; set; } = [];
}

public class RejectVenueRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class AdminDashboardDto
{
    public int TotalVenues { get; set; }
    public int PendingVenues { get; set; }
    public int ApprovedVenues { get; set; }
    public int RejectedVenues { get; set; }
    public int TotalCourts { get; set; }
    public int TotalBookings { get; set; }
    public int TotalPlayers { get; set; }
    public int TotalOwners { get; set; }
    public List<AdminVenueDto> RecentSubmissions { get; set; } = [];
    public List<AdminModerationActionDto> RecentActivity { get; set; } = [];
}

public class AdminModerationActionDto
{
    public Guid Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? AdminName { get; set; }
    public DateTime Timestamp { get; set; }
}

public class AdminVenueDetailsDto
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string OwnerPhone { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Country { get; set; } = "Egypt";
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public VenueApprovalStatus ApprovalStatus { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedByName { get; set; }
    public bool IsActive { get; set; }
    public bool IsVerified { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<AdminCourtDto> Courts { get; set; } = [];
    public List<string> Amenities { get; set; } = [];
    public List<AdminOperatingHourDto> OperatingHours { get; set; } = [];
    public string? CancellationPolicyDescription { get; set; }
    public int FreeCancellationHours { get; set; }
    public decimal LateCancellationFeePercent { get; set; }
}

public class AdminCourtDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public SportType SportType { get; set; }
    public decimal PricePerHour { get; set; }
    public string SurfaceType { get; set; } = string.Empty;
    public bool IsIndoor { get; set; }
    public int Capacity { get; set; }
    public bool IsActive { get; set; }
}

public class AdminOperatingHourDto
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly OpenTime { get; set; }
    public TimeOnly CloseTime { get; set; }
    public bool IsClosed { get; set; }
}

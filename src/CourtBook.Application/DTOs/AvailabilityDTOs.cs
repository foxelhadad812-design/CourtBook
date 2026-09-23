using System.Text.Json.Serialization;

namespace CourtBook.Application.DTOs;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SlotAvailabilityStatus
{
    Available,
    Booked,
    Past,
    Closed
}

public class AvailabilitySlotDto
{
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public SlotAvailabilityStatus Status { get; set; }
    public decimal BasePrice { get; set; }
    public decimal EffectivePrice { get; set; }
    public bool IsPeak { get; set; }
    public string? AppliedRuleName { get; set; }
}

public class CourtAvailabilityRequest
{
    public DateOnly Date { get; set; }
    public int DurationMinutes { get; set; } = 60;
}

public class CourtAvailabilityResponse
{
    public Guid CourtId { get; set; }
    public string CourtName { get; set; } = string.Empty;
    public string SportType { get; set; } = string.Empty;
    public Guid VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string VenueCity { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public int DurationMinutes { get; set; }
    public int FreeCancellationHours { get; set; } = 24;
    public string CancellationPolicyDescription { get; set; } = string.Empty;
    public List<AvailabilitySlotDto> Slots { get; set; } = [];
}

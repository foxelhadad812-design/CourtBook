namespace CourtBook.Application.DTOs;

public class CreateCourtRequest
{
    public string Name { get; set; } = string.Empty;
    public string SportType { get; set; } = string.Empty; // Using string for Enum input
    public decimal PricePerHour { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UpdateCourtRequest
{
    public string Name { get; set; } = string.Empty;
    public string SportType { get; set; } = string.Empty;
    public decimal PricePerHour { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CourtResponse
{
    public Guid Id { get; set; }
    public Guid VenueId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SportType { get; set; } = string.Empty;
    public decimal PricePerHour { get; set; }
    public bool IsActive { get; set; }
    public List<ScheduleResponse> Schedules { get; set; } = [];
}

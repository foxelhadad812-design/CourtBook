namespace CourtBook.Domain.Entities;

public class OperatingHour
{
    public Guid Id { get; set; }
    public Guid VenueId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly OpenTime { get; set; }
    public TimeOnly CloseTime { get; set; }
    public bool IsClosed { get; set; } = false;

    // Navigation property
    public Venue Venue { get; set; } = null!;
}

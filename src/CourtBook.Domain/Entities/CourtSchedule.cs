namespace CourtBook.Domain.Entities;

/// <summary>
/// Defines the opening and closing times for a court on a specific day of the week.
/// </summary>
public class CourtSchedule
{
    public Guid Id { get; set; }
    public Guid CourtId { get; set; }       // FK → Court
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly OpenTime { get; set; }
    public TimeOnly CloseTime { get; set; }

    // Navigation property
    public Court Court { get; set; } = null!;
}

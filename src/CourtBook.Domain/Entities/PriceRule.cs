namespace CourtBook.Domain.Entities;

public class PriceRule
{
    public Guid Id { get; set; }
    public Guid CourtId { get; set; }
    public string Name { get; set; } = string.Empty; // e.g. "Peak Evening", "Weekend Morning"
    public DayOfWeek? DayOfWeek { get; set; }         // null means applies to all days
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public decimal PriceMultiplier { get; set; } = 1.0m; // e.g. 1.25 (+25%)
    public decimal? FixedPrice { get; set; }              // If set, overrides multiplier
    public bool IsActive { get; set; } = true;

    // Navigation property
    public Court Court { get; set; } = null!;
}

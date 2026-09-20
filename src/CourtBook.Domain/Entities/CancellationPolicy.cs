namespace CourtBook.Domain.Entities;

public class CancellationPolicy
{
    public Guid Id { get; set; }
    public Guid VenueId { get; set; }
    public int FreeCancellationHours { get; set; } = 24; // Hours before start time
    public decimal LateCancellationFeePercent { get; set; } = 50.0m; // Charge 50% if cancelled late
    public string PolicyDescription { get; set; } = "Free cancellation up to 24 hours before your booking.";

    // Navigation property
    public Venue Venue { get; set; } = null!;
}

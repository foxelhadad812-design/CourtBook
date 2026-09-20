using CourtBook.Domain.Enums;

namespace CourtBook.Domain.Entities;

/// <summary>
/// A bookable court within a venue. Tracks sport type, pricing, and availability.
/// </summary>
public class Court
{
    public Guid Id { get; set; }
    public Guid VenueId { get; set; }       // FK → Venue
    public string Name { get; set; } = string.Empty;
    public SportType SportType { get; set; }
    public decimal PricePerHour { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public Venue Venue { get; set; } = null!;
    public ICollection<CourtSchedule> Schedules { get; set; } = [];
    public ICollection<Booking> Bookings { get; set; } = [];
}

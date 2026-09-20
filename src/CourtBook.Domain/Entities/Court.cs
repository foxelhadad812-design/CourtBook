using CourtBook.Domain.Enums;

namespace CourtBook.Domain.Entities;

/// <summary>
/// A bookable court or pitch within a sports facility.
/// Tracks sport type, surface, indoor/outdoor, dynamic price rules, and availability.
/// </summary>
public class Court
{
    public Guid Id { get; set; }
    public Guid VenueId { get; set; }       // FK → Venue
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public SportType SportType { get; set; }
    public decimal PricePerHour { get; set; }
    public string SurfaceType { get; set; } = "Artificial Grass"; // e.g. "Natural Grass", "Artificial Turf", "Glass/Plexi", "Clay", "Hard Court", "Wood"
    public bool IsIndoor { get; set; } = false;
    public int Capacity { get; set; } = 10; // Max players supported
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Venue Venue { get; set; } = null!;
    public ICollection<CourtSchedule> Schedules { get; set; } = [];
    public ICollection<PriceRule> PriceRules { get; set; } = [];
    public ICollection<CourtImage> Images { get; set; } = [];
    public ICollection<Booking> Bookings { get; set; } = [];
    public ICollection<Game> Games { get; set; } = [];
}

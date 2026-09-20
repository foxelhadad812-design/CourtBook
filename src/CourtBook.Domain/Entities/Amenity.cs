namespace CourtBook.Domain.Entities;

public class Amenity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty; // e.g. "Free Parking", "Showers", "Locker Room", "Cafe", "WiFi", "Equipment Rental", "Floodlights"
    public string Icon { get; set; } = string.Empty; // Bootstrap Icon class e.g. "bi-p-square"
    public string Category { get; set; } = "General"; // "Facility", "Comfort", "Sport"

    // Navigation property
    public ICollection<VenueAmenity> VenueAmenities { get; set; } = [];
}

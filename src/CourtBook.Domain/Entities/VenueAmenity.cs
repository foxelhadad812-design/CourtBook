namespace CourtBook.Domain.Entities;

public class VenueAmenity
{
    public Guid VenueId { get; set; }
    public Guid AmenityId { get; set; }

    // Navigation properties
    public Venue Venue { get; set; } = null!;
    public Amenity Amenity { get; set; } = null!;
}

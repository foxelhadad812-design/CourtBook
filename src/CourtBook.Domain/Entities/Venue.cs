namespace CourtBook.Domain.Entities;

/// <summary>
/// A sports facility owned by an Owner user.
/// Contains courts, amenities, operating hours, cancellation policies, reviews, and images.
/// </summary>
public class Venue
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }       // FK → User
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Country { get; set; } = "Egypt";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Website { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsVerified { get; set; } = false;
    public double AverageRating { get; set; } = 0.0;
    public int TotalReviews { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User Owner { get; set; } = null!;
    public ICollection<Court> Courts { get; set; } = [];
    public ICollection<VenueImage> Images { get; set; } = [];
    public ICollection<VenueAmenity> Amenities { get; set; } = [];
    public ICollection<OperatingHour> OperatingHours { get; set; } = [];
    public CancellationPolicy? CancellationPolicy { get; set; }
    public ICollection<Review> Reviews { get; set; } = [];
    public ICollection<Favorite> Favorites { get; set; } = [];
    public ICollection<Game> Games { get; set; } = [];
}

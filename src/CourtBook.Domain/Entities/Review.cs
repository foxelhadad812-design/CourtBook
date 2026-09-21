namespace CourtBook.Domain.Entities;

public class Review
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }  // Only users with completed booking can review!
    public Guid UserId { get; set; }
    public Guid VenueId { get; set; }
    
    // Ratings (1 to 5)
    public int OverallRating { get; set; }
    public int CourtQualityRating { get; set; }
    public int CleanlinessRating { get; set; }
    public int StaffRating { get; set; }
    public int ValueRating { get; set; }

    public string? Comment { get; set; }
    public string? OwnerResponse { get; set; }
    public DateTime? OwnerRespondedAt { get; set; }
    public bool IsModerated { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Booking Booking { get; set; } = null!;
    public User User { get; set; } = null!;
    public Venue Venue { get; set; } = null!;
}

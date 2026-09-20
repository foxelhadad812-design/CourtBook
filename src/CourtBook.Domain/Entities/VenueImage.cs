namespace CourtBook.Domain.Entities;

public class VenueImage
{
    public Guid Id { get; set; }
    public Guid VenueId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public bool IsPrimary { get; set; } = false;
    public int DisplayOrder { get; set; } = 0;
    public string? Caption { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public Venue Venue { get; set; } = null!;
}

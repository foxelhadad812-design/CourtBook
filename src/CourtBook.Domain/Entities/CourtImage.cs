namespace CourtBook.Domain.Entities;

public class CourtImage
{
    public Guid Id { get; set; }
    public Guid CourtId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public bool IsPrimary { get; set; } = false;
    public int DisplayOrder { get; set; } = 0;
    public string? Caption { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public Court Court { get; set; } = null!;
}

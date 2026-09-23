namespace CourtBook.Domain.Entities;

public class CourtAddon
{
    public Guid Id { get; set; }
    public Guid CourtId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public decimal Price { get; set; }
    public string Unit { get; set; } = "Item"; // e.g. "Racket", "Can", "Hour"
    public bool IsAvailable { get; set; } = true;

    public Court Court { get; set; } = null!;
}

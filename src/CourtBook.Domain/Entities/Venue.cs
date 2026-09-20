namespace CourtBook.Domain.Entities;

/// <summary>
/// A venue owned by a user with the Owner role. Contains one or more courts.
/// </summary>
public class Venue
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }       // FK → User
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;

    // Navigation properties
    public User Owner { get; set; } = null!;
    public ICollection<Court> Courts { get; set; } = [];
}

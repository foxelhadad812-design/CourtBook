using CourtBook.Domain.Enums;

namespace CourtBook.Domain.Entities;

/// <summary>
/// Represents a registered user. Can be an Admin, Owner, or Client.
/// </summary>
public class User
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public Role Role { get; set; }

    // Navigation properties
    public ICollection<Venue> OwnedVenues { get; set; } = [];
    public ICollection<Booking> Bookings { get; set; } = [];
}

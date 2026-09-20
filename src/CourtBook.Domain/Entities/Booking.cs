using CourtBook.Domain.Enums;

namespace CourtBook.Domain.Entities;

/// <summary>
/// Represents a court reservation made by a client.
/// Overlap prevention is enforced in application code; a DB constraint will be added in a later phase.
/// </summary>
public class Booking
{
    public Guid Id { get; set; }
    public Guid CourtId { get; set; }       // FK → Court
    public Guid UserId { get; set; }        // FK → User (client)
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public decimal TotalPrice { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Court Court { get; set; } = null!;
    public User User { get; set; } = null!;
}

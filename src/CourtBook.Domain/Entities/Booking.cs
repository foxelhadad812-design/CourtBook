using CourtBook.Domain.Enums;

namespace CourtBook.Domain.Entities;

/// <summary>
/// Represents a court reservation made by a player.
/// Includes human-readable booking reference, payment state, and relations to reviews and payments.
/// </summary>
public class Booking
{
    public Guid Id { get; set; }
    public string BookingReference { get; set; } = string.Empty; // e.g. "PS-20260921-A1B2C"
    public Guid CourtId { get; set; }                           // FK → Court
    public Guid UserId { get; set; }                            // FK → User (player)
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public decimal TotalPrice { get; set; }
    public decimal CancellationFee { get; set; } = 0;
    public string? CancellationReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Court Court { get; set; } = null!;
    public User User { get; set; } = null!;
    public Payment? Payment { get; set; }
    public Review? Review { get; set; }
}

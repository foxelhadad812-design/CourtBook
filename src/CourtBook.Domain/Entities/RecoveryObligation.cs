using CourtBook.Domain.Enums;

namespace CourtBook.Domain.Entities;

/// <summary>
/// Auditable recovery obligation created when a post-settlement or post-payout refund causes a deficit.
/// Invariants:
/// - RemainingDeficitAmount decreases as future settlements or manual payments offset it.
/// - When RemainingDeficitAmount reaches 0, Status becomes Recovered.
/// - OutstandingDeficit on OwnerBalance equals the sum of RemainingDeficitAmount for all Active obligations.
/// </summary>
public class RecoveryObligation
{
    public Guid Id { get; set; }
    public string ObligationReference { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public Guid BookingId { get; set; }
    public Guid PaymentId { get; set; }
    public decimal TotalDeficitAmount { get; set; }
    public decimal RemainingDeficitAmount { get; set; }
    public string Currency { get; set; } = "EGP";
    public RecoveryStatus Status { get; set; } = RecoveryStatus.Active;
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public Guid? ResolvedBySettlementBatchId { get; set; }
    public string? AdminNotes { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    // Navigation properties
    public User Owner { get; set; } = null!;
    public Booking Booking { get; set; } = null!;
    public Payment Payment { get; set; } = null!;
}

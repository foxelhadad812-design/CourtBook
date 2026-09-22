namespace CourtBook.Domain.Entities;

/// <summary>
/// Settled booking financial position.
/// Invariant: Exactly one SettlementItem per BookingId (enforced via unique index).
/// </summary>
public class SettlementItem
{
    public Guid Id { get; set; }
    public Guid SettlementBatchId { get; set; }
    public Guid BookingId { get; set; }
    public Guid PaymentId { get; set; }
    public Guid OwnerId { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal CommissionAmount { get; set; }
    public decimal NetAmount { get; set; }
    public DateTime SettledAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public SettlementBatch SettlementBatch { get; set; } = null!;
    public Booking Booking { get; set; } = null!;
    public Payment Payment { get; set; } = null!;
    public User Owner { get; set; } = null!;
}

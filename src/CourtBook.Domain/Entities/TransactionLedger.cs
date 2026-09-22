using CourtBook.Domain.Enums;

namespace CourtBook.Domain.Entities;

/// <summary>
/// Immutable financial ledger entry recording every money movement.
/// Never delete ledger entries — create correcting entries instead.
/// </summary>
public class TransactionLedger
{
    public Guid Id { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? BookingId { get; set; }
    public Guid? PayoutRequestId { get; set; }
    public Guid UserId { get; set; }       // Player or Owner who triggered the transaction
    public Guid OwnerId { get; set; }      // Venue owner
    public LedgerEntryType EntryType { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal CommissionAmount { get; set; }
    public decimal NetAmount { get; set; }  // GrossAmount - CommissionAmount
    public decimal CommissionRateSnapshot { get; set; } // Rate at time of transaction
    public string Currency { get; set; } = "EGP";
    public string? Description { get; set; }
    public string? ProviderReference { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Payment? Payment { get; set; }
    public PayoutRequest? PayoutRequest { get; set; }
}

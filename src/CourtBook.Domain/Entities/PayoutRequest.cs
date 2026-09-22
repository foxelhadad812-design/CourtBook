using CourtBook.Domain.Enums;

namespace CourtBook.Domain.Entities;

/// <summary>
/// Owner payout request lifecycle entity.
/// Invariants:
/// - Amount is deducted from AvailableBalance and held in InFlightBalance on Submission.
/// - Paid requires a non-empty ExternalTransactionReference.
/// - AdminId != OwnerId for approval/disbursement.
/// </summary>
public class PayoutRequest
{
    public Guid Id { get; set; }
    public string PayoutReference { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public Guid PayoutMethodId { get; set; }
    public decimal Amount { get; set; }
    public decimal Fee { get; set; } = 0m;
    public decimal NetAmount { get; set; }
    public string Currency { get; set; } = "EGP";
    public PayoutStatus Status { get; set; } = PayoutStatus.Submitted;
    public string? RejectionReason { get; set; }
    public string? ExternalTransactionReference { get; set; }
    public string? DisbursementNote { get; set; }
    public Guid? ApprovedByAdminId { get; set; }
    public Guid? DisbursedByAdminId { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    // Navigation properties
    public User Owner { get; set; } = null!;
    public OwnerPayoutMethod PayoutMethod { get; set; } = null!;
    public User? ApprovedByAdmin { get; set; }
    public User? DisbursedByAdmin { get; set; }
    public ICollection<TransactionLedger> LedgerEntries { get; set; } = new List<TransactionLedger>();
}

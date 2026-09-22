using CourtBook.Domain.Enums;

namespace CourtBook.Domain.Entities;

public class Payment
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public PaymentMethod Method { get; set; } = PaymentMethod.PayAtFacility;
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? TransactionReference { get; set; }

    /// <summary>Provider-assigned order/transaction ID for gateway lookup.</summary>
    public string? ProviderOrderId { get; set; }

    /// <summary>URL to redirect player to complete online payment (set during initiation).</summary>
    public string? PaymentUrl { get; set; }

    /// <summary>When the payment hold expires (10-minute window for online payments).</summary>
    public DateTime? ExpiresAt { get; set; }

    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Platform commission amount retained (calculated server-side).</summary>
    public decimal CommissionAmount { get; set; } = 0;

    /// <summary>Amount that belongs to owner after commission deduction.</summary>
    public decimal OwnerNetAmount { get; set; } = 0;

    // Navigation properties
    public Booking Booking { get; set; } = null!;
    public ICollection<TransactionLedger> LedgerEntries { get; set; } = new List<TransactionLedger>();
}

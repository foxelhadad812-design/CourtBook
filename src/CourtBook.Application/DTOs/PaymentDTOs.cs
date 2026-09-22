using CourtBook.Domain.Enums;

namespace CourtBook.Application.DTOs;

// ─── Initiation ────────────────────────────────────────────────────────────

public class InitiatePaymentRequest
{
    public Guid BookingId { get; set; }
    /// <summary>PayAtFacility | CreditCard | DebitCard | DigitalWallet | Fawry</summary>
    public string PaymentMethod { get; set; } = "PayAtFacility";
}

public class InitiatePaymentResponse
{
    public bool Success { get; set; }
    public Guid PaymentId { get; set; }
    public Guid BookingId { get; set; }
    public string? PaymentUrl { get; set; }         // null for PayAtFacility
    public string? ProviderOrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public string PaymentMethod { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }        // 10-min hold for online
    public string? ErrorMessage { get; set; }
}

// ─── Verification ──────────────────────────────────────────────────────────

public class PaymentVerificationResponse
{
    public bool IsSuccessful { get; set; }
    public Guid BookingId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? TransactionReference { get; set; }
    public decimal Amount { get; set; }
    public string? ErrorMessage { get; set; }
}

// ─── Details ───────────────────────────────────────────────────────────────

public class PaymentDetailsResponse
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public string Method { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? TransactionReference { get; set; }
    public string? ProviderOrderId { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

// ─── Refund ────────────────────────────────────────────────────────────────

public class RefundResponse
{
    public bool Success { get; set; }
    public string? RefundTransactionId { get; set; }
    public decimal RefundAmount { get; set; }
    public string? ErrorMessage { get; set; }
}

// ─── Ledger / Financial Reporting ──────────────────────────────────────────

public class TransactionLedgerDto
{
    public Guid Id { get; set; }
    public Guid PaymentId { get; set; }
    public Guid BookingId { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public string EntryType { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public decimal CommissionAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal CommissionRateSnapshot { get; set; }
    public string Currency { get; set; } = "EGP";
    public string? Description { get; set; }
    public string? ProviderReference { get; set; }
    public DateTime CreatedAt { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string VenueName { get; set; } = string.Empty;
}

public class OwnerFinancialReportDto
{
    public Guid OwnerId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public decimal TotalGross { get; set; }
    public decimal TotalCommission { get; set; }
    public decimal TotalNet { get; set; }
    public decimal TotalRefunds { get; set; }
    public decimal TotalCancellationFees { get; set; }
    public int TotalTransactions { get; set; }
    public List<TransactionLedgerDto> Entries { get; set; } = new();
}

public class AdminFinancialSummaryDto
{
    public decimal TotalGrossRevenue { get; set; }
    public decimal TotalCommissionCollected { get; set; }
    public decimal TotalRefundsIssued { get; set; }
    public decimal TotalNetToOwners { get; set; }
    public int TotalTransactions { get; set; }
    public int CompletedPayments { get; set; }
    public int FailedPayments { get; set; }
    public int RefundedPayments { get; set; }
}

// ─── Webhook ───────────────────────────────────────────────────────────────

public class WebhookPayload
{
    public string? Type { get; set; }
    public string? OrderId { get; set; }
    public string? TransactionId { get; set; }
    public bool? Success { get; set; }
    public decimal? Amount { get; set; }
    public string? HmacSha512 { get; set; }
}

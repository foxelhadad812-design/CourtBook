namespace CourtBook.Application.DTOs;

public class OwnerBalanceDto
{
    public Guid OwnerId { get; set; }
    public decimal PendingBalance { get; set; }
    public decimal AvailableBalance { get; set; }
    public decimal InFlightBalance { get; set; }
    public decimal TotalPaidOut { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal OutstandingDeficit { get; set; }
    public string Currency { get; set; } = "EGP";
    public DateTime UpdatedAt { get; set; }
}

public class PayoutMethodDto
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string AccountHolderName { get; set; } = string.Empty;
    public string? BankName { get; set; }
    public string? MaskedIban { get; set; }
    public string? MaskedAccountNumber { get; set; }
    public string? MaskedInstaPayAddress { get; set; }
    public string? MaskedMobileWalletNumber { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public bool IsVerified { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreatePayoutMethodRequest
{
    public string Type { get; set; } = string.Empty; // "BankTransfer", "InstaPay", "MobileWallet"
    public string AccountHolderName { get; set; } = string.Empty;
    public string? BankName { get; set; }
    public string? Iban { get; set; }
    public string? AccountNumber { get; set; }
    public string? InstaPayAddress { get; set; }
    public string? MobileWalletNumber { get; set; }
    public bool IsDefault { get; set; } = false;
}

public class PayoutRequestDto
{
    public Guid Id { get; set; }
    public string PayoutReference { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public Guid PayoutMethodId { get; set; }
    public string PayoutMethodType { get; set; } = string.Empty;
    public string DestinationSummary { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Fee { get; set; }
    public decimal NetAmount { get; set; }
    public string Currency { get; set; } = "EGP";
    public string Status { get; set; } = string.Empty;
    public string? RejectionReason { get; set; }
    public string? ExternalTransactionReference { get; set; }
    public string? DisbursementNote { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? PaidAt { get; set; }
}

public class CreatePayoutRequest
{
    public Guid PayoutMethodId { get; set; }
    public decimal Amount { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class RejectPayoutRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class MarkPayoutPaidRequest
{
    public string ExternalTransactionReference { get; set; } = string.Empty;
    public string? DisbursementNote { get; set; }
}

public class AdminPayoutSummaryDto
{
    public decimal TotalPendingReviewAmount { get; set; }
    public int PendingReviewCount { get; set; }
    public decimal TotalApprovedAmount { get; set; }
    public int ApprovedCount { get; set; }
    public decimal TotalPaidAmount { get; set; }
    public int PaidCount { get; set; }
}

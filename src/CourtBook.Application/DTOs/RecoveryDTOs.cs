namespace CourtBook.Application.DTOs;

public class RecoveryObligationDto
{
    public Guid Id { get; set; }
    public string ObligationReference { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public Guid BookingId { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public Guid PaymentId { get; set; }
    public decimal TotalDeficitAmount { get; set; }
    public decimal RemainingDeficitAmount { get; set; }
    public string Currency { get; set; } = "EGP";
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public Guid? ResolvedBySettlementBatchId { get; set; }
    public string? AdminNotes { get; set; }
}

public class ManualSettleRecoveryRequest
{
    public decimal Amount { get; set; }
    public string ExternalReference { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class WriteOffRecoveryRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class RecoverySummaryDto
{
    public decimal TotalActiveDeficit { get; set; }
    public int ActiveObligationsCount { get; set; }
    public decimal TotalRecoveredAmount { get; set; }
    public decimal TotalWrittenOffAmount { get; set; }
}

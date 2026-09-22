namespace CourtBook.Application.DTOs;

public class SettlementBatchDto
{
    public Guid Id { get; set; }
    public string BatchReference { get; set; } = string.Empty;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public decimal TotalGross { get; set; }
    public decimal TotalCommission { get; set; }
    public decimal TotalNet { get; set; }
    public int ItemCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public List<SettlementItemDto> Items { get; set; } = new();
}

public class SettlementItemDto
{
    public Guid Id { get; set; }
    public Guid SettlementBatchId { get; set; }
    public Guid BookingId { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public Guid PaymentId { get; set; }
    public Guid OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public decimal CommissionAmount { get; set; }
    public decimal NetAmount { get; set; }
    public DateTime SettledAt { get; set; }
}

public class RunSettlementRequest
{
    /// <summary>
    /// Optional clearing buffer in hours. Defaults to 24 hours if null or not specified.
    /// </summary>
    public int? BufferHours { get; set; } = 24;
}

public class SettlementSummaryDto
{
    public decimal TotalSettledAmount { get; set; }
    public int TotalSettledBatches { get; set; }
    public int TotalSettledItems { get; set; }
    public DateTime? LastSettlementDate { get; set; }
}

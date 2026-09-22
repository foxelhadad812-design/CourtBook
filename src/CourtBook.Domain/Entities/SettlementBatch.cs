using CourtBook.Domain.Enums;

namespace CourtBook.Domain.Entities;

/// <summary>
/// Immutable batch container for settled booking financial positions.
/// </summary>
public class SettlementBatch
{
    public Guid Id { get; set; }
    public string BatchReference { get; set; } = string.Empty;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public decimal TotalGross { get; set; }
    public decimal TotalCommission { get; set; }
    public decimal TotalNet { get; set; }
    public int ItemCount { get; set; }
    public SettlementStatus Status { get; set; } = SettlementStatus.Completed;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid CreatedBy { get; set; }

    // Navigation properties
    public ICollection<SettlementItem> Items { get; set; } = new List<SettlementItem>();
}

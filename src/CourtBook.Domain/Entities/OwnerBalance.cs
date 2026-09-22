namespace CourtBook.Domain.Entities;

/// <summary>
/// Materialized financial balance for a venue owner.
/// Invariants:
/// - AvailableBalance >= 0 (deficits are tracked in OutstandingDeficit).
/// - OutstandingDeficit == SUM(RemainingDeficitAmount of active RecoveryObligations).
/// - TotalRefunded is an informational metric tracking lifetime owner-net refunds.
/// </summary>
public class OwnerBalance
{
    public Guid OwnerId { get; set; }
    public decimal PendingBalance { get; set; } = 0m;
    public decimal AvailableBalance { get; set; } = 0m;
    public decimal InFlightBalance { get; set; } = 0m;
    public decimal TotalPaidOut { get; set; } = 0m;
    public decimal TotalRefunded { get; set; } = 0m;
    public decimal OutstandingDeficit { get; set; } = 0m;
    public string Currency { get; set; } = "EGP";
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User Owner { get; set; } = null!;
}

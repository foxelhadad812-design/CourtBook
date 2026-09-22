namespace CourtBook.Domain.Entities;

/// <summary>
/// Prevents duplicate processing of the same gateway event.
/// Unique constraint on (Provider + ProviderTransactionId).
/// </summary>
public class IdempotencyLog
{
    public Guid Id { get; set; }

    /// <summary>Payment provider name (e.g., "Paymob", "Fawry").</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Provider's unique transaction/event identifier.</summary>
    public string ProviderTransactionId { get; set; } = string.Empty;

    /// <summary>HTTP status code returned to the provider on first processing.</summary>
    public int ResponseStatusCode { get; set; } = 200;

    /// <summary>Short summary of what was done (e.g., "PaymentCompleted", "RefundProcessed").</summary>
    public string Action { get; set; } = string.Empty;

    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

    // Optional link to the payment that was affected
    public Guid? PaymentId { get; set; }
}

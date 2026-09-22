namespace CourtBook.Application.Interfaces;

/// <summary>
/// Provider-agnostic payment gateway abstraction.
/// Implementations must never trust client-supplied amounts.
/// </summary>
public interface IPaymentGatewayService
{
    /// <summary>
    /// Initiates an online payment order with the provider.
    /// Returns a redirect URL and provider order ID.
    /// </summary>
    Task<PaymentInitiationResult> InitiatePaymentAsync(PaymentInitiationRequest request);

    /// <summary>
    /// Verifies a payment using the provider's server-side API (never trusts browser callbacks alone).
    /// </summary>
    Task<PaymentVerificationResult> VerifyPaymentAsync(string providerOrderId);

    /// <summary>
    /// Issues a refund through the provider for a given transaction.
    /// </summary>
    Task<RefundResult> RefundAsync(RefundRequest request);

    /// <summary>
    /// Validates an incoming webhook HMAC signature.
    /// Throws if signature is invalid — never process unsigned webhooks.
    /// </summary>
    bool ValidateWebhookSignature(string payload, string signature);

    /// <summary>Provider name for idempotency log.</summary>
    string ProviderName { get; }
}

public record PaymentInitiationRequest(
    Guid BookingId,
    decimal Amount,
    string Currency,
    string CustomerEmail,
    string CustomerName,
    string CustomerPhone,
    string ReturnUrl,
    string CallbackUrl);

public record PaymentInitiationResult(
    bool Success,
    string? PaymentUrl,
    string? ProviderOrderId,
    string? ErrorMessage);

public record PaymentVerificationResult(
    bool IsSuccessful,
    string? TransactionReference,
    decimal? AmountPaid,
    string? ProviderStatus,
    string? ErrorMessage);

public record RefundRequest(
    string ProviderTransactionId,
    decimal Amount,
    string Reason);

public record RefundResult(
    bool Success,
    string? RefundTransactionId,
    string? ErrorMessage);

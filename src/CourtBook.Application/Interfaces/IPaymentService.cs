using CourtBook.Application.Common;
using CourtBook.Application.DTOs;

namespace CourtBook.Application.Interfaces;

/// <summary>
/// Application-level payment service: manages payment lifecycle, ledger, commission, and notifications.
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Initiates an online payment for a booking.
    /// Server calculates amount from booking data — never from client input.
    /// </summary>
    Task<InitiatePaymentResponse> InitiateOnlinePaymentAsync(Guid userId, Guid bookingId, string paymentMethod, string returnBaseUrl);

    /// <summary>
    /// Processes a payment method selection for PayAtFacility (no gateway needed).
    /// </summary>
    Task<InitiatePaymentResponse> SelectPayAtFacilityAsync(Guid userId, Guid bookingId);

    /// <summary>
    /// Called by the webhook engine after HMAC verification.
    /// Marks payment as completed and records ledger entries.
    /// </summary>
    Task ProcessWebhookPaymentCompletedAsync(string providerOrderId, string transactionRef, decimal amountPaid, string idempotencyKey, string provider);

    /// <summary>
    /// Server-side return-URL verification (never trust browser alone).
    /// </summary>
    Task<PaymentVerificationResponse> VerifyReturnAsync(string providerOrderId);

    /// <summary>
    /// Initiates a refund for a cancelled booking.
    /// </summary>
    Task<RefundResponse> ProcessRefundAsync(Guid bookingId, string reason);

    /// <summary>Gets payment details for a booking (player-scoped).</summary>
    Task<PaymentDetailsResponse?> GetPaymentByBookingAsync(Guid userId, Guid bookingId);

    /// <summary>Gets paginated transaction history for admin financial reporting.</summary>
    Task<Application.Common.PagedResult<TransactionLedgerDto>> GetAdminTransactionHistoryAsync(int page, int pageSize, string? status, DateTime? from, DateTime? to);

    /// <summary>Gets owner financial report (strictly scoped to owner's venues).</summary>
    Task<OwnerFinancialReportDto> GetOwnerFinancialReportAsync(Guid ownerId, DateTime? from, DateTime? to);
}

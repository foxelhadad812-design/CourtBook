using System.Security.Claims;
using System.Text;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CourtBook.API.Controllers;

/// <summary>
/// Payment controller — handles payment initiation, webhook callbacks, and financial reporting.
///
/// SECURITY RULES:
/// - Webhook endpoint has no JWT requirement (provider can't authenticate with JWT).
/// - HMAC is validated before any processing — unsigned requests are rejected.
/// - Return URL alone is NOT proof of payment — server always re-verifies with gateway.
/// - Server calculates all amounts — never trusts client-supplied prices.
/// </summary>
[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly IPaymentGatewayService _gateway;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        IPaymentService paymentService,
        IPaymentGatewayService gateway,
        ILogger<PaymentsController> logger)
    {
        _paymentService = paymentService;
        _gateway        = gateway;
        _logger         = logger;
    }

    // ── Initiation ──────────────────────────────────────────────────────────

    /// <summary>
    /// Initiates an online payment for a booking.
    /// Amount is calculated server-side from booking data.
    /// </summary>
    [HttpPost("initiate")]
    [Authorize]
    [EnableRateLimiting("api")]
    public async Task<IActionResult> InitiatePayment([FromBody] InitiatePaymentRequest request)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return Unauthorized();

        try
        {
            var returnBaseUrl = $"{Request.Scheme}://{Request.Host}";

            InitiatePaymentResponse result;
            if (string.Equals(request.PaymentMethod, "PayAtFacility", StringComparison.OrdinalIgnoreCase))
            {
                result = await _paymentService.SelectPayAtFacilityAsync(userId, request.BookingId);
            }
            else
            {
                result = await _paymentService.InitiateOnlinePaymentAsync(
                    userId, request.BookingId, request.PaymentMethod, returnBaseUrl);
            }

            if (!result.Success)
                return BadRequest(new { error = result.ErrorMessage });

            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Booking not found." });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    // ── Return URL Verification ─────────────────────────────────────────────

    /// <summary>
    /// Server-side verification after browser returns from payment page.
    /// Never trust browser callback alone — always re-verify with gateway.
    /// </summary>
    [HttpGet("verify")]
    [Authorize]
    public async Task<IActionResult> VerifyReturn([FromQuery] string orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return BadRequest(new { error = "orderId is required." });

        var result = await _paymentService.VerifyReturnAsync(orderId);
        return Ok(result);
    }

    // ── Webhook ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Provider webhook endpoint. No JWT required (provider callback).
    /// HMAC signature is verified before any processing.
    /// Idempotency prevents duplicate event handling.
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    [EnableRateLimiting("api")]
    public async Task<IActionResult> Webhook([FromHeader(Name = "X-Hmac-Signature")] string? signature)
    {
        // Read raw body for HMAC validation
        string rawBody;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
        {
            rawBody = await reader.ReadToEndAsync();
        }

        if (string.IsNullOrWhiteSpace(rawBody))
        {
            _logger.LogWarning("Webhook received with empty body.");
            return BadRequest();
        }

        // HMAC validation — reject unsigned webhooks
        if (!_gateway.ValidateWebhookSignature(rawBody, signature ?? string.Empty))
        {
            _logger.LogWarning("Webhook HMAC validation failed. Rejecting.");
            return Unauthorized(new { error = "Invalid webhook signature." });
        }

        WebhookPayload? payload;
        try
        {
            payload = System.Text.Json.JsonSerializer.Deserialize<WebhookPayload>(rawBody,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Webhook deserialization failed.");
            return BadRequest();
        }

        if (payload?.OrderId == null || payload.TransactionId == null)
        {
            _logger.LogWarning("Webhook missing required fields.");
            return BadRequest();
        }

        // Only process successful payment events
        if (payload.Success == true && payload.Amount.HasValue)
        {
            try
            {
                await _paymentService.ProcessWebhookPaymentCompletedAsync(
                    providerOrderId:  payload.OrderId,
                    transactionRef:   payload.TransactionId,
                    amountPaid:       payload.Amount.Value / 100m, // provider sends cents
                    idempotencyKey:   payload.TransactionId,
                    provider:         _gateway.ProviderName);
            }
            catch (Exception ex)
            {
                // Log but return 200 to prevent provider retry storms
                _logger.LogError(ex, "Webhook processing error for order {OrderId}", payload.OrderId);
            }
        }

        // Always return 200 to acknowledge receipt
        return Ok();
    }

    // ── Payment Details ─────────────────────────────────────────────────────

    [HttpGet("booking/{bookingId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetPaymentByBooking(Guid bookingId)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return Unauthorized();

        var payment = await _paymentService.GetPaymentByBookingAsync(userId, bookingId);
        if (payment is null) return NotFound();

        return Ok(payment);
    }

    // ── Admin Financial Reporting ───────────────────────────────────────────

    [HttpGet("admin/transactions")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAdminTransactions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var result = await _paymentService.GetAdminTransactionHistoryAsync(page, pageSize, status, from, to);
        return Ok(result);
    }

    // ── Owner Financial Reporting ───────────────────────────────────────────

    [HttpGet("owner/report")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> GetOwnerReport(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var ownerId = GetUserId();
        if (ownerId == Guid.Empty) return Unauthorized();

        var report = await _paymentService.GetOwnerFinancialReportAsync(ownerId, from, to);
        return Ok(report);
    }

    // ── Refund (Admin only) ─────────────────────────────────────────────────

    [HttpPost("booking/{bookingId:guid}/refund")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RefundBooking(Guid bookingId, [FromBody] RefundBookingRequest? request)
    {
        try
        {
            var result = await _paymentService.ProcessRefundAsync(bookingId, request?.Reason ?? "Admin-initiated refund");
            if (!result.Success)
                return BadRequest(new { error = result.ErrorMessage });
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refund failed for booking {BookingId}", bookingId);
            return StatusCode(500, new { error = "Refund processing error." });
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }
}

public class RefundBookingRequest
{
    public string? Reason { get; set; }
}

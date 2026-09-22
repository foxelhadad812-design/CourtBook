using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using CourtBook.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CourtBook.Infrastructure.Services;

/// <summary>
/// Paymob payment gateway integration.
/// Uses sandbox credentials in development; production keys must be injected via environment variables.
/// NEVER commit real API keys to source control.
/// </summary>
public class PaymobGatewayService : IPaymentGatewayService
{
    private readonly HttpClient _http;
    private readonly ILogger<PaymobGatewayService> _logger;
    private readonly string _apiKey;
    private readonly string _integrationId;
    private readonly string _iframeId;
    private readonly string _hmacSecret;
    private readonly bool _isSandbox;

    public string ProviderName => "Paymob";

    public PaymobGatewayService(
        HttpClient http,
        IConfiguration configuration,
        ILogger<PaymobGatewayService> logger)
    {
        _http = http;
        _logger = logger;

        var section = configuration.GetSection("PaymentGateway:Paymob");
        _apiKey        = section["ApiKey"]        ?? "PAYMOB_SANDBOX_API_KEY_PLACEHOLDER";
        _integrationId = section["IntegrationId"] ?? "PAYMOB_INTEGRATION_ID_PLACEHOLDER";
        _iframeId      = section["IframeId"]      ?? "PAYMOB_IFRAME_ID_PLACEHOLDER";
        _hmacSecret    = section["HmacSecret"]    ?? "PAYMOB_HMAC_SECRET_PLACEHOLDER";
        _isSandbox     = bool.TryParse(section["IsSandbox"], out var sb) && sb;

        // NOTE: Log only that gateway is configured — never log credentials
        _logger.LogInformation("PaymobGatewayService initialized. Sandbox={IsSandbox}", _isSandbox);
    }

    public async Task<PaymentInitiationResult> InitiatePaymentAsync(PaymentInitiationRequest request)
    {
        try
        {
            // Step 1: Authentication token
            var authToken = await GetAuthTokenAsync();
            if (authToken == null)
                return new PaymentInitiationResult(false, null, null, "Failed to authenticate with payment gateway.");

            // Step 2: Order registration
            var orderId = await RegisterOrderAsync(authToken, request);
            if (orderId == null)
                return new PaymentInitiationResult(false, null, null, "Failed to register order with payment gateway.");

            // Step 3: Payment key
            var paymentKey = await GetPaymentKeyAsync(authToken, orderId, request);
            if (paymentKey == null)
                return new PaymentInitiationResult(false, null, null, "Failed to obtain payment key from gateway.");

            // Step 4: Build iframe URL
            var baseUrl = _isSandbox
                ? "https://accept.paymobsolutions.com/api/acceptance/iframes"
                : "https://accept.paymob.com/api/acceptance/iframes";

            var paymentUrl = $"{baseUrl}/{_iframeId}?payment_token={paymentKey}";

            return new PaymentInitiationResult(true, paymentUrl, orderId, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating Paymob payment for booking {BookingId}", request.BookingId);
            return new PaymentInitiationResult(false, null, null, "Payment gateway error. Please try again.");
        }
    }

    public async Task<PaymentVerificationResult> VerifyPaymentAsync(string providerOrderId)
    {
        try
        {
            var authToken = await GetAuthTokenAsync();
            if (authToken == null)
                return new PaymentVerificationResult(false, null, null, null, "Gateway auth failed.");

            var baseUrl = _isSandbox
                ? "https://accept.paymobsolutions.com/api/ecommerce/orders"
                : "https://accept.paymob.com/api/ecommerce/orders";

            var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/{providerOrderId}");
            req.Headers.Add("Authorization", $"Bearer {authToken}");

            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return new PaymentVerificationResult(false, null, null, null, "Gateway order lookup failed.");

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var isPaid = root.TryGetProperty("paid_amount_cents", out var paidCentsEl) && paidCentsEl.GetInt64() > 0;
            var paidAmount = isPaid ? paidCentsEl.GetInt64() / 100m : (decimal?)null;
            var transactionRef = root.TryGetProperty("id", out var idEl) ? idEl.ToString() : null;

            return new PaymentVerificationResult(isPaid, transactionRef, paidAmount, isPaid ? "PAID" : "UNPAID", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying Paymob payment for order {OrderId}", providerOrderId);
            return new PaymentVerificationResult(false, null, null, null, "Verification error.");
        }
    }

    public async Task<RefundResult> RefundAsync(RefundRequest request)
    {
        try
        {
            var authToken = await GetAuthTokenAsync();
            if (authToken == null)
                return new RefundResult(false, null, "Gateway auth failed.");

            var baseUrl = _isSandbox
                ? "https://accept.paymobsolutions.com/api/acceptance/void_refund/refund"
                : "https://accept.paymob.com/api/acceptance/void_refund/refund";

            var body = new
            {
                auth_token = authToken,
                transaction_id = request.ProviderTransactionId,
                amount_cents = (int)(request.Amount * 100)
            };

            var resp = await _http.PostAsJsonAsync(baseUrl, body);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Refund failed for transaction {TxId}, HTTP {Status}",
                    request.ProviderTransactionId, resp.StatusCode);
                return new RefundResult(false, null, "Gateway refund failed.");
            }

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var refundTxId = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.ToString() : null;

            return new RefundResult(true, refundTxId, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing refund for transaction {TxId}", request.ProviderTransactionId);
            return new RefundResult(false, null, "Refund processing error.");
        }
    }

    public bool ValidateWebhookSignature(string payload, string incomingSignature)
    {
        if (string.IsNullOrWhiteSpace(_hmacSecret) ||
            _hmacSecret == "PAYMOB_HMAC_SECRET_PLACEHOLDER")
        {
            _logger.LogWarning("HMAC secret not configured — rejecting webhook for security.");
            return false;
        }

        var hmac = HMACSHA512.HashData(Encoding.UTF8.GetBytes(_hmacSecret), Encoding.UTF8.GetBytes(payload));
        var computed = Convert.ToHexString(hmac).ToLowerInvariant();
        return string.Equals(computed, incomingSignature?.ToLowerInvariant(), StringComparison.Ordinal);
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private async Task<string?> GetAuthTokenAsync()
    {
        var baseUrl = _isSandbox
            ? "https://accept.paymobsolutions.com/api/auth/tokens"
            : "https://accept.paymob.com/api/auth/tokens";

        var body = new { api_key = _apiKey };
        var resp = await _http.PostAsJsonAsync(baseUrl, body);
        if (!resp.IsSuccessStatusCode) return null;

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("token", out var tok) ? tok.GetString() : null;
    }

    private async Task<string?> RegisterOrderAsync(string authToken, PaymentInitiationRequest request)
    {
        var baseUrl = _isSandbox
            ? "https://accept.paymobsolutions.com/api/ecommerce/orders"
            : "https://accept.paymob.com/api/ecommerce/orders";

        var body = new
        {
            auth_token = authToken,
            delivery_needed = false,
            amount_cents = (int)(request.Amount * 100),
            currency = request.Currency,
            merchant_order_id = request.BookingId.ToString(),
            items = Array.Empty<object>()
        };

        var resp = await _http.PostAsJsonAsync(baseUrl, body);
        if (!resp.IsSuccessStatusCode) return null;

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.ToString() : null;
    }

    private async Task<string?> GetPaymentKeyAsync(string authToken, string orderId, PaymentInitiationRequest request)
    {
        var baseUrl = _isSandbox
            ? "https://accept.paymobsolutions.com/api/acceptance/payment_keys"
            : "https://accept.paymob.com/api/acceptance/payment_keys";

        var body = new
        {
            auth_token = authToken,
            amount_cents = (int)(request.Amount * 100),
            expiration = 3600,
            order_id = orderId,
            billing_data = new
            {
                first_name = request.CustomerName.Split(' ').FirstOrDefault() ?? "Player",
                last_name = request.CustomerName.Split(' ').LastOrDefault() ?? "User",
                email = request.CustomerEmail,
                phone_number = string.IsNullOrWhiteSpace(request.CustomerPhone) ? "N/A" : request.CustomerPhone,
                country = "EG",
                city = "Cairo",
                street = "N/A",
                building = "N/A",
                floor = "N/A",
                apartment = "N/A"
            },
            currency = request.Currency,
            integration_id = _integrationId
        };

        var resp = await _http.PostAsJsonAsync(baseUrl, body);
        if (!resp.IsSuccessStatusCode) return null;

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("token", out var tok) ? tok.GetString() : null;
    }
}

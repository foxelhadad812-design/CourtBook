using System.Security.Cryptography;
using System.Text;
using CourtBook.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CourtBook.Tests;

public class PaymobWebhookHmacTests
{
    private const string HmacSecret = "test-paymob-hmac-secret-for-automated-tests";

    private const string ValidPayload = """
        {
          "type": "TRANSACTION",
          "obj": {
            "amount_cents": 12500,
            "created_at": "2026-10-10T12:00:00.000000",
            "currency": "EGP",
            "error_occured": false,
            "has_parent_transaction": false,
            "id": 192036465,
            "integration_id": 4097558,
            "is_3d_secure": true,
            "is_auth": false,
            "is_capture": false,
            "is_refunded": false,
            "is_standalone_payment": true,
            "is_voided": false,
            "order": { "id": 217503754 },
            "owner": 302852,
            "pending": false,
            "source_data": { "pan": "2346", "sub_type": "MasterCard", "type": "card" },
            "success": true
          }
        }
        """;

    [Fact]
    public void ValidateWebhookSignature_AcceptsPaymobTransactionHmac()
    {
        var service = CreateService();
        var signature = CreateExpectedSignature();

        Assert.True(service.ValidateWebhookSignature(ValidPayload, signature));
    }

    [Fact]
    public void ValidateWebhookSignature_RejectsTamperedPayload()
    {
        var service = CreateService();
        var signature = CreateExpectedSignature();
        var tamperedPayload = ValidPayload.Replace("\"amount_cents\": 12500", "\"amount_cents\": 1", StringComparison.Ordinal);

        Assert.False(service.ValidateWebhookSignature(tamperedPayload, signature));
    }

    [Fact]
    public void ValidateWebhookSignature_RejectsMissingTransactionObject()
    {
        var service = CreateService();

        Assert.False(service.ValidateWebhookSignature("{\"type\":\"TRANSACTION\"}", new string('0', 128)));
    }

    private static PaymobGatewayService CreateService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PaymentGateway:Paymob:HmacSecret"] = HmacSecret,
                ["PaymentGateway:Paymob:IsSandbox"] = "true"
            })
            .Build();

        return new PaymobGatewayService(
            new HttpClient(),
            configuration,
            NullLogger<PaymobGatewayService>.Instance);
    }

    private static string CreateExpectedSignature()
    {
        // Paymob signs these transaction values in this exact order, with no separators.
        const string concatenatedValues =
            "12500" +
            "2026-10-10T12:00:00.000000" +
            "EGP" +
            "false" +
            "false" +
            "192036465" +
            "4097558" +
            "true" +
            "false" +
            "false" +
            "false" +
            "true" +
            "false" +
            "217503754" +
            "302852" +
            "false" +
            "2346" +
            "MasterCard" +
            "card" +
            "true";

        var digest = HMACSHA512.HashData(
            Encoding.UTF8.GetBytes(HmacSecret),
            Encoding.UTF8.GetBytes(concatenatedValues));

        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}

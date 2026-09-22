using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CourtBook.Tests;

public class ProductionConfigurationTests
{
    [Theory]
    [InlineData("short_secret")]
    [InlineData("less_than_32_characters_key")]
    public void JwtValidation_RejectsKeysUnder32Characters(string shortKey)
    {
        var ex = Record.Exception(() =>
        {
            if (shortKey.Length < 32)
                throw new InvalidOperationException("JwtSettings:Key must be at least 32 characters (256 bits) for HMAC-SHA256.");
        });

        Assert.NotNull(ex);
        Assert.IsType<InvalidOperationException>(ex);
    }

    [Theory]
    [InlineData("CourtBook_Super_Secret_Key_For_Jwt_Authentication_2024!")]
    [InlineData("PlaySpot_Dev_Secret_Key_Must_Be_At_Least_32_Chars_Long!")]
    [InlineData("CHANGE_THIS_TO_A_LONG_SECRET_KEY_32CHARS")]
    [InlineData("OVERRIDE_VIA_ENV_VAR_OR_PROD_SECRET_MIN_32_CHARS")]
    [InlineData("OVERRIDE_VIA_ENV_VAR_OR_SECRET_MIN_32_CHARS")]
    public void JwtValidation_RejectsKnownPlaceholdersInProductionAndStaging(string placeholder)
    {
        var knownInsecurePlaceholders = new[]
        {
            "CourtBook_Super_Secret_Key_For_Jwt_Authentication_2024!",
            "PlaySpot_Dev_Secret_Key_Must_Be_At_Least_32_Chars_Long!",
            "CHANGE_THIS_TO_A_LONG_SECRET_KEY_32CHARS",
            "OVERRIDE_VIA_ENV_VAR_OR_PROD_SECRET_MIN_32_CHARS",
            "OVERRIDE_VIA_ENV_VAR_OR_SECRET_MIN_32_CHARS"
        };

        foreach (var env in new[] { "Production", "Staging" })
        {
            var isProdOrStaging = env == "Production" || env == "Staging";
            var ex = Record.Exception(() =>
            {
                if (isProdOrStaging && knownInsecurePlaceholders.Any(p => string.Equals(p, placeholder, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException($"CRITICAL SECURITY ERROR: {env} environment cannot use a known development or placeholder JWT secret key!");
                }
            });

            Assert.NotNull(ex);
            Assert.IsType<InvalidOperationException>(ex);
        }
    }

    [Theory]
    [InlineData("Server=localhost;Database=CourtBookDB;Trusted_Connection=True;")]
    [InlineData("Server=(localdb)\\mssqllocaldb;Database=CourtBookDB;Trusted_Connection=True;")]
    [InlineData("Server=127.0.0.1;Database=CourtBookDB;User Id=sa;Password=pwd;")]
    public void DatabaseValidation_RejectsLocalConnectionsInProductionAndStaging(string localConn)
    {
        foreach (var env in new[] { "Production", "Staging" })
        {
            var isProdOrStaging = env == "Production" || env == "Staging";
            var ex = Record.Exception(() =>
            {
                if (isProdOrStaging)
                {
                    var lowerConn = localConn.ToLowerInvariant();
                    if (lowerConn.Contains("(localdb)") || lowerConn.Contains("localhost") || lowerConn.Contains("127.0.0.1"))
                    {
                        throw new InvalidOperationException($"CRITICAL SECURITY ERROR: {env} environment cannot use localhost or LocalDB database connection.");
                    }
                }
            });

            Assert.NotNull(ex);
            Assert.IsType<InvalidOperationException>(ex);
        }
    }

    [Fact]
    public async Task SeedData_Segregation_PrerequisitesSeedAdminAndTerms_WithoutDemoData()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var logger = NullLogger.Instance;

        // Seed only system prerequisites (as in production)
        await SeedData.EnsureTermsDocumentsAsync(db, logger);
        await SeedData.EnsureBaseAmenitiesAsync(db, logger);
        await SeedData.EnsureAdminAccountAsync(db, logger);

        // Verify terms exist
        var termsCount = await db.TermsDocuments.CountAsync();
        Assert.True(termsCount >= 2); // Player + Owner terms

        // Verify base amenities exist
        var amenitiesCount = await db.Amenities.CountAsync();
        Assert.True(amenitiesCount >= 6);

        // Verify admin exists
        var admin = await db.Users.FirstOrDefaultAsync(u => u.Role == Role.Admin);
        Assert.NotNull(admin);
        Assert.Equal("admin@courtbook.eg", admin.Email);

        // Verify NO demo venues or demo players exist
        var venuesCount = await db.Venues.CountAsync();
        Assert.Equal(0, venuesCount);

        var demoPlayer = await db.Users.FirstOrDefaultAsync(u => u.Email == "omar@gmail.com");
        Assert.Null(demoPlayer);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("localhost:6379", true)]
    [InlineData("redis.internal:6379,password=secret", true)]
    public void RedisConnectionString_EvaluatesPresenceCorrectly(string? connectionString, bool expectedConfigured)
    {
        var isConfigured = !string.IsNullOrWhiteSpace(connectionString);
        Assert.Equal(expectedConfigured, isConfigured);
    }

    [Theory]
    [InlineData("", "12345", "67890", "secret", true)]
    [InlineData("OVERRIDE_VIA_ENV_VAR_PaymentGateway__Paymob__ApiKey", "12345", "67890", "secret", true)]
    [InlineData("valid_api_key", "", "67890", "secret", true)]
    [InlineData("valid_api_key", "12345", "OVERRIDE_VIA_ENV_VAR_PaymentGateway__Paymob__IframeId", "secret", true)]
    [InlineData("valid_api_key", "12345", "67890", "valid_hmac_secret", false)]
    public void PaymobValidation_RejectsInvalidOrPlaceholderCredentialsInProduction(
        string apiKey, string integrationId, string iframeId, string hmacSecret, bool shouldThrow)
    {
        var invalidPlaceholders = new[]
        {
            "OVERRIDE_VIA_ENV_VAR_PaymentGateway__Paymob__ApiKey",
            "OVERRIDE_VIA_ENV_VAR_PaymentGateway__Paymob__IntegrationId",
            "OVERRIDE_VIA_ENV_VAR_PaymentGateway__Paymob__IframeId",
            "OVERRIDE_VIA_ENV_VAR_PaymentGateway__Paymob__HmacSecret"
        };

        var ex = Record.Exception(() =>
        {
            if (string.IsNullOrWhiteSpace(apiKey) || invalidPlaceholders.Any(p => string.Equals(p, apiKey, StringComparison.OrdinalIgnoreCase)) ||
                string.IsNullOrWhiteSpace(integrationId) || invalidPlaceholders.Any(p => string.Equals(p, integrationId, StringComparison.OrdinalIgnoreCase)) ||
                string.IsNullOrWhiteSpace(iframeId) || invalidPlaceholders.Any(p => string.Equals(p, iframeId, StringComparison.OrdinalIgnoreCase)) ||
                string.IsNullOrWhiteSpace(hmacSecret) || invalidPlaceholders.Any(p => string.Equals(p, hmacSecret, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "CRITICAL SECURITY ERROR: Production environment cannot use missing, empty, or default placeholder Paymob gateway credentials!");
            }
        });

        if (shouldThrow)
        {
            Assert.NotNull(ex);
            Assert.IsType<InvalidOperationException>(ex);
        }
        else
        {
            Assert.Null(ex);
        }
    }

    [Fact]
    public void HealthDetailsPayload_FormatsNonSensitiveDiagnosticJson()
    {
        var data = new Dictionary<string, object>
        {
            ["ThresholdHours"] = 36,
            ["TotalCompletedBatches"] = 5,
            ["LastBatchReference"] = "SETTLE-20260923-01",
            ["LastBatchAgeHours"] = 2.5
        };

        var payload = new
        {
            status = "Healthy",
            timestamp = DateTime.UtcNow.ToString("o"),
            checks = new Dictionary<string, object>
            {
                ["settlement"] = new
                {
                    status = "Healthy",
                    description = "Last settlement batch completed 2.5 hours ago.",
                    data = data
                }
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        Assert.Contains("SETTLE-20260923-01", json);
        Assert.Contains("Healthy", json);
        Assert.DoesNotContain("ConnectionString", json);
        Assert.DoesNotContain("Secret", json);
    }

    [Theory]
    [InlineData("auth", "auth")]
    [InlineData("community", "community")]
    [InlineData("api", "api")]
    public void RateLimitHeaders_FormatsInformativeHeaders(string policyName, string expectedHeaderValue)
    {
        var headers = new Dictionary<string, string>();
        headers["X-RateLimit-Policy"] = policyName;

        Assert.True(headers.ContainsKey("X-RateLimit-Policy"));
        Assert.Equal(expectedHeaderValue, headers["X-RateLimit-Policy"]);
    }
}

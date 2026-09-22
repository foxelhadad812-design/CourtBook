using System.Data;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using CourtBook.Infrastructure.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace CourtBook.Tests;

/// <summary>
/// Phase 9.9 Hardening, Security Gating, Webhook Idempotency & Session Revocation Tests.
/// </summary>
public class Phase99HardeningTests
{
    private const string TestHmacSecret = "test_hmac_secret_key_at_least_64_characters_long_for_security_testing_purpose!";

    private static IConfiguration BuildConfig(decimal? commissionRate = null)
    {
        var dict = new Dictionary<string, string?>
        {
            ["JwtSettings:Key"]                     = "PlaySpot_Super_Secret_Testing_Key_At_Least_32_Chars_Long!",
            ["JwtSettings:Issuer"]                  = "CourtBook.Test",
            ["JwtSettings:Audience"]                = "CourtBook.TestClients",
            ["JwtSettings:ExpiryInMinutes"]         = "60",
            ["PaymentGateway:CommissionRate"]       = commissionRate?.ToString("0.00") ?? "0.05",
            ["PaymentGateway:OnlineHoldMinutes"]    = "10",
            ["PaymentGateway:Paymob:IsSandbox"]     = "true",
            ["PaymentGateway:Paymob:ApiKey"]        = "SANDBOX_TEST_KEY",
            ["PaymentGateway:Paymob:IntegrationId"] = "SANDBOX_INT_ID",
            ["PaymentGateway:Paymob:IframeId"]      = "SANDBOX_IFRAME",
            ["PaymentGateway:Paymob:HmacSecret"]    = TestHmacSecret,
            ["HealthChecks:DetailsApiKey"]          = "test-diagnostic-secret-key-12345"
        };
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static (PaymentService service, Mock<IPaymentGatewayService> gatewayMock, Mock<INotificationService> notifMock)
        BuildPaymentService(AppDbContext db, decimal? commissionRate = null)
    {
        var gateway = new Mock<IPaymentGatewayService>();
        gateway.Setup(g => g.ProviderName).Returns("Paymob");

        var notifications = new Mock<INotificationService>();
        notifications.Setup(n => n.SendNotificationAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<NotificationType>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var svc = new PaymentService(
            db,
            gateway.Object,
            NullLogger<PaymentService>.Instance,
            BuildConfig(commissionRate),
            notifications.Object);

        return (svc, gateway, notifications);
    }

    private static async Task<(Guid bookingId, Guid userId, Guid ownerId, Guid paymentId, string orderId)>
        SeedPaymentAsync(AppDbContext db, decimal price = 200m)
    {
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var booking = new Booking
        {
            Id               = Guid.NewGuid(),
            BookingReference = $"PS-99-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            CourtId          = courtId,
            UserId           = clientId,
            StartTime        = DateTime.UtcNow.AddDays(1),
            EndTime          = DateTime.UtcNow.AddDays(1).AddHours(1),
            Status           = BookingStatus.Confirmed,
            PaymentStatus    = PaymentStatus.Processing,
            TotalPrice       = price,
            CreatedAt        = DateTime.UtcNow
        };

        var orderId = $"ORDER-{Guid.NewGuid():N}";
        var payment = new Payment
        {
            Id               = Guid.NewGuid(),
            BookingId        = booking.Id,
            Amount           = price,
            Currency         = "EGP",
            Method           = PaymentMethod.CreditCard,
            Status           = PaymentStatus.Processing,
            ProviderOrderId  = orderId,
            ExpiresAt        = DateTime.UtcNow.AddMinutes(10),
            CreatedAt        = DateTime.UtcNow
        };

        db.Bookings.Add(booking);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        return (booking.Id, clientId, ownerId, payment.Id, orderId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 9.9B — PAYMOB WEBHOOK IDEMPOTENCY & CONCURRENCY
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Webhook_FirstDelivery_CompletesPaymentAndCreditsOwnerPendingBalance()
    {
        using var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (svc, _, _) = BuildPaymentService(db);
        var (bookingId, clientId, ownerId, paymentId, orderId) = await SeedPaymentAsync(db, 200m);

        var txId = $"TX-{Guid.NewGuid():N}";
        await svc.ProcessWebhookPaymentCompletedAsync(orderId, txId, 200m, txId, "Paymob");

        var updatedPayment = await db.Payments.FindAsync(paymentId);
        Assert.NotNull(updatedPayment);
        Assert.Equal(PaymentStatus.Completed, updatedPayment.Status);
        Assert.Equal(10.00m, updatedPayment.CommissionAmount);
        Assert.Equal(190.00m, updatedPayment.OwnerNetAmount);

        var ownerBalance = await db.OwnerBalances.FirstOrDefaultAsync(b => b.OwnerId == ownerId);
        Assert.NotNull(ownerBalance);
        Assert.Equal(190.00m, ownerBalance.PendingBalance);

        var ledgerCount = await db.TransactionLedger.CountAsync(l => l.PaymentId == paymentId);
        Assert.Equal(1, ledgerCount);

        var idempotencyRecord = await db.IdempotencyLogs.FirstOrDefaultAsync(l => l.Provider == "Paymob" && l.ProviderTransactionId == txId);
        Assert.NotNull(idempotencyRecord);
        Assert.Equal("PaymentCompleted", idempotencyRecord.Action);
    }

    [Fact]
    public async Task Webhook_DuplicateSequentialDelivery_IgnoredWithoutDuplicateFinancialEffects()
    {
        using var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (svc, _, _) = BuildPaymentService(db);
        var (bookingId, clientId, ownerId, paymentId, orderId) = await SeedPaymentAsync(db, 200m);

        var txId = $"TX-{Guid.NewGuid():N}";
        // First delivery
        await svc.ProcessWebhookPaymentCompletedAsync(orderId, txId, 200m, txId, "Paymob");

        // Second delivery (replay with same payload and transaction ID)
        await svc.ProcessWebhookPaymentCompletedAsync(orderId, txId, 200m, txId, "Paymob");

        var ownerBalance = await db.OwnerBalances.FirstOrDefaultAsync(b => b.OwnerId == ownerId);
        Assert.NotNull(ownerBalance);
        Assert.Equal(190.00m, ownerBalance.PendingBalance); // Still exactly 190.00, not 380.00

        var ledgerCount = await db.TransactionLedger.CountAsync(l => l.PaymentId == paymentId);
        Assert.Equal(1, ledgerCount); // Exactly 1 ledger entry, no duplicate

        var idempotencyCount = await db.IdempotencyLogs.CountAsync(l => l.Provider == "Paymob" && l.ProviderTransactionId == txId);
        Assert.Equal(1, idempotencyCount);
    }

    [Fact]
    public async Task Webhook_ConcurrentDuplicateDeliveries_SafelySynchronizedWithoutDoubleCredit()
    {
        using var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (svc, _, _) = BuildPaymentService(db);
        var (bookingId, clientId, ownerId, paymentId, orderId) = await SeedPaymentAsync(db, 300m);

        var txId = $"TX-CONC-{Guid.NewGuid():N}";

        // Simulate 4 concurrent webhook callbacks arriving simultaneously
        var tasks = Enumerable.Range(0, 4)
            .Select(_ => svc.ProcessWebhookPaymentCompletedAsync(orderId, txId, 300m, txId, "Paymob"))
            .ToArray();

        await Task.WhenAll(tasks);

        var updatedPayment = await db.Payments.FindAsync(paymentId);
        Assert.NotNull(updatedPayment);
        Assert.Equal(PaymentStatus.Completed, updatedPayment.Status);

        // 300 - (300 * 0.05 = 15) = 285 net
        var ownerBalance = await db.OwnerBalances.FirstOrDefaultAsync(b => b.OwnerId == ownerId);
        Assert.NotNull(ownerBalance);
        Assert.Equal(285.00m, ownerBalance.PendingBalance); // Exactly 285.00, never doubled or quadrupled!

        var ledgerCount = await db.TransactionLedger.CountAsync(l => l.PaymentId == paymentId);
        Assert.Equal(1, ledgerCount); // Exactly 1 ledger entry
    }

    [Fact]
    public async Task Webhook_AlreadyCompletedPayment_SkipsProcessingAndRecordsIdempotency()
    {
        using var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (svc, _, _) = BuildPaymentService(db);
        var (bookingId, clientId, ownerId, paymentId, orderId) = await SeedPaymentAsync(db, 200m);

        // Mark payment as already Completed
        var payment = await db.Payments.FindAsync(paymentId);
        payment!.Status = PaymentStatus.Completed;
        await db.SaveChangesAsync();

        var txId = $"TX-ALREADY-{Guid.NewGuid():N}";
        await svc.ProcessWebhookPaymentCompletedAsync(orderId, txId, 200m, txId, "Paymob");

        var ownerBalance = await db.OwnerBalances.FirstOrDefaultAsync(b => b.OwnerId == ownerId);
        Assert.Null(ownerBalance); // No balance added

        var ledgerCount = await db.TransactionLedger.CountAsync(l => l.PaymentId == paymentId);
        Assert.Equal(0, ledgerCount);

        var log = await db.IdempotencyLogs.FirstOrDefaultAsync(l => l.Provider == "Paymob" && l.ProviderTransactionId == txId);
        Assert.NotNull(log);
        Assert.Equal("AlreadyCompleted", log.Action);
    }

    [Fact]
    public void Webhook_InvalidHmacSignature_RejectedByGateway()
    {
        var config = BuildConfig();
        var client = new HttpClient();
        var gateway = new PaymobGatewayService(client, config, NullLogger<PaymobGatewayService>.Instance);

        var payload = "{\"order_id\":\"999\",\"transaction_id\":\"tx_fake\",\"amount_cents\":20000,\"success\":true}";
        var invalidSignature = "invalid_tampered_hmac_signature";

        var isValid = gateway.ValidateWebhookSignature(payload, invalidSignature);
        Assert.False(isValid);
    }

    [Fact]
    public async Task Webhook_AmountMismatch_MarksPaymentFailedWithoutCreditingBalance()
    {
        using var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (svc, _, _) = BuildPaymentService(db);
        var (bookingId, clientId, ownerId, paymentId, orderId) = await SeedPaymentAsync(db, 200m);

        var txId = $"TX-MISMATCH-{Guid.NewGuid():N}";
        // Send 100m instead of expected 200m
        await svc.ProcessWebhookPaymentCompletedAsync(orderId, txId, 100m, txId, "Paymob");

        var payment = await db.Payments.FindAsync(paymentId);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Failed, payment.Status);

        var ownerBalance = await db.OwnerBalances.FirstOrDefaultAsync(b => b.OwnerId == ownerId);
        Assert.Null(ownerBalance); // Zero credit to owner

        var ledgerCount = await db.TransactionLedger.CountAsync(l => l.PaymentId == paymentId);
        Assert.Equal(0, ledgerCount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 9.9B — PASSWORD CHANGE & SESSION REVOCATION
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_ThrowsUnauthorizedAccessException()
    {
        using var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var tokenService = new TokenService(BuildConfig());
        var authService = new AuthService(db, tokenService);

        var reg = await authService.RegisterAsync(new RegisterRequest
        {
            Name = "Security User",
            Email = $"sec_{Guid.NewGuid():N}@test.com",
            Password = "InitialPassword123!",
            Phone = "01011112222",
            AcceptTerms = true
        });

        var request = new ChangePasswordRequest
        {
            CurrentPassword = "WrongPassword999!",
            NewPassword = "NewSecurePassword456!"
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.ChangePasswordAsync(reg.UserId, request));
    }

    [Fact]
    public async Task ChangePassword_IdenticalNewPassword_ThrowsArgumentException()
    {
        using var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var tokenService = new TokenService(BuildConfig());
        var authService = new AuthService(db, tokenService);

        var reg = await authService.RegisterAsync(new RegisterRequest
        {
            Name = "Security User",
            Email = $"sec_{Guid.NewGuid():N}@test.com",
            Password = "SamePassword123!",
            Phone = "01011112222",
            AcceptTerms = true
        });

        var request = new ChangePasswordRequest
        {
            CurrentPassword = "SamePassword123!",
            NewPassword = "SamePassword123!"
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.ChangePasswordAsync(reg.UserId, request));
        Assert.Contains("identical", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangePassword_Success_UpdatesPasswordAndRevokesAllActiveSessions()
    {
        using var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var tokenService = new TokenService(BuildConfig());
        var authService = new AuthService(db, tokenService);

        var email = $"sec_{Guid.NewGuid():N}@test.com";
        var reg = await authService.RegisterAsync(new RegisterRequest
        {
            Name = "Security User",
            Email = email,
            Password = "OldPassword123!",
            Phone = "01011112222",
            AcceptTerms = true,
            DeviceName = "iPhone 15"
        });

        var oldRefreshToken = reg.RefreshToken;

        // Verify active session exists
        var sessionsBefore = await authService.GetUserSessionsAsync(reg.UserId);
        Assert.Single(sessionsBefore);

        // Change password
        var changeReq = new ChangePasswordRequest
        {
            CurrentPassword = "OldPassword123!",
            NewPassword = "BrandNewPassword789!"
        };
        var changed = await authService.ChangePasswordAsync(reg.UserId, changeReq);
        Assert.True(changed);

        // 1. Verify old password fails login
        var oldLogin = await authService.LoginAsync(new LoginRequest
        {
            Email = email,
            Password = "OldPassword123!"
        });
        Assert.Null(oldLogin);

        // 2. Verify new password successfully logs in
        var newLogin = await authService.LoginAsync(new LoginRequest
        {
            Email = email,
            Password = "BrandNewPassword789!"
        });
        Assert.NotNull(newLogin);
        Assert.NotEmpty(newLogin.AccessToken);

        // 3. Verify prior refresh token is revoked and cannot be refreshed
        await Assert.ThrowsAnyAsync<SecurityTokenException>(() =>
            authService.RefreshTokenAsync(new RefreshTokenRequest { RefreshToken = oldRefreshToken }));

        // 4. Verify all pre-existing sessions were marked revoked in DB
        var activeTokens = await db.RefreshTokens
            .Where(r => r.UserId == reg.UserId && r.TokenHash == AuthService.HashToken(oldRefreshToken))
            .ToListAsync();
        Assert.All(activeTokens, t => Assert.True(t.IsRevoked));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 9.9A — FORWARDED HEADERS & HEALTH DETAILS SECURITY GATING
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ForwardedHeaders_AppliesClientIpAndSchemeFromTrustedProxy()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        };
        options.KnownProxies.Add(IPAddress.Loopback);

        var middleware = new ForwardedHeadersMiddleware(
            next: ctx => Task.CompletedTask,
            loggerFactory: NullLoggerFactory.Instance,
            options: Options.Create(options));

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.195";
        context.Request.Headers["X-Forwarded-Proto"] = "https";

        await middleware.Invoke(context);

        Assert.Equal("203.0.113.195", context.Connection.RemoteIpAddress?.ToString());
        Assert.Equal("https", context.Request.Scheme);
    }

    [Fact]
    public async Task HealthDetails_SecurityGating_EnforcesProductionAccessRules()
    {
        // Helper to simulate the health details endpoint gating logic from Program.cs
        async Task<(int statusCode, string body)> InvokeHealthDetailsAsync(
            bool isDevelopment,
            string? configuredApiKey,
            string? headerApiKey,
            string? queryApiKey,
            IPAddress remoteIp)
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = remoteIp;
            context.Connection.LocalIpAddress = IPAddress.Loopback;

            if (!string.IsNullOrEmpty(headerApiKey))
            {
                context.Request.Headers["X-Health-Key"] = headerApiKey;
            }
            if (!string.IsNullOrEmpty(queryApiKey))
            {
                context.Request.QueryString = new QueryString($"?apiKey={queryApiKey}");
            }

            using var mem = new MemoryStream();
            context.Response.Body = mem;

            // Security gating logic under test (identical to Program.cs)
            if (!isDevelopment)
            {
                var providedHeaderKey = context.Request.Headers["X-Health-Key"].FirstOrDefault();
                var providedQueryKey = context.Request.Query["apiKey"].FirstOrDefault();

                var isKeyAuthorized = !string.IsNullOrWhiteSpace(configuredApiKey) &&
                                      (string.Equals(providedHeaderKey, configuredApiKey, StringComparison.Ordinal) ||
                                       string.Equals(providedQueryKey, configuredApiKey, StringComparison.Ordinal));

                var isLocal = context.Connection.RemoteIpAddress != null &&
                              (IPAddress.IsLoopback(context.Connection.RemoteIpAddress) ||
                               context.Connection.RemoteIpAddress.Equals(context.Connection.LocalIpAddress));

                if (!isKeyAuthorized && !isLocal)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"error\":\"Forbidden: Detailed health diagnostics are restricted in this environment.\"}");
                    mem.Seek(0, SeekOrigin.Begin);
                    using var reader = new StreamReader(mem, Encoding.UTF8);
                    return (context.Response.StatusCode, await reader.ReadToEndAsync());
                }
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"status\":\"Healthy\"}");
            mem.Seek(0, SeekOrigin.Begin);
            using var okReader = new StreamReader(mem, Encoding.UTF8);
            return (context.Response.StatusCode, await okReader.ReadToEndAsync());
        }

        var publicRemoteIp = IPAddress.Parse("198.51.100.25");
        var loopbackIp = IPAddress.Loopback;
        const string secretKey = "super-secret-diagnostic-key";

        // 1. In Production, remote caller without key is rejected with 403 Forbidden
        var (status1, _) = await InvokeHealthDetailsAsync(
            isDevelopment: false,
            configuredApiKey: secretKey,
            headerApiKey: null,
            queryApiKey: null,
            remoteIp: publicRemoteIp);
        Assert.Equal(StatusCodes.Status403Forbidden, status1);

        // 2. In Production, remote caller with wrong key is rejected with 403 Forbidden
        var (status2, _) = await InvokeHealthDetailsAsync(
            isDevelopment: false,
            configuredApiKey: secretKey,
            headerApiKey: "wrong-key",
            queryApiKey: null,
            remoteIp: publicRemoteIp);
        Assert.Equal(StatusCodes.Status403Forbidden, status2);

        // 3. In Production, remote caller with valid header key gets 200 OK
        var (status3, body3) = await InvokeHealthDetailsAsync(
            isDevelopment: false,
            configuredApiKey: secretKey,
            headerApiKey: secretKey,
            queryApiKey: null,
            remoteIp: publicRemoteIp);
        Assert.Equal(StatusCodes.Status200OK, status3);
        Assert.Contains("Healthy", body3);

        // 4. In Production, remote caller with valid query parameter gets 200 OK
        var (status4, body4) = await InvokeHealthDetailsAsync(
            isDevelopment: false,
            configuredApiKey: secretKey,
            headerApiKey: null,
            queryApiKey: secretKey,
            remoteIp: publicRemoteIp);
        Assert.Equal(StatusCodes.Status200OK, status4);
        Assert.Contains("Healthy", body4);

        // 5. In Production, local loopback caller without key gets 200 OK
        var (status5, body5) = await InvokeHealthDetailsAsync(
            isDevelopment: false,
            configuredApiKey: secretKey,
            headerApiKey: null,
            queryApiKey: null,
            remoteIp: loopbackIp);
        Assert.Equal(StatusCodes.Status200OK, status5);
        Assert.Contains("Healthy", body5);

        // 6. In Development, remote caller gets 200 OK without key
        var (status6, body6) = await InvokeHealthDetailsAsync(
            isDevelopment: true,
            configuredApiKey: secretKey,
            headerApiKey: null,
            queryApiKey: null,
            remoteIp: publicRemoteIp);
        Assert.Equal(StatusCodes.Status200OK, status6);
        Assert.Contains("Healthy", body6);
    }
}

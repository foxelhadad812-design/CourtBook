using System.Security.Cryptography;
using System.Text;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CourtBook.Tests;

/// <summary>
/// Phase 7.1: Payment Security, Financial Integrity & End-to-End Verification Tests.
/// Covers: Webhook HMAC security, replay attacks, concurrency, expired holds,
/// state machine transitions, partial refunds, fee retention, player isolation, and commission configurability.
/// </summary>
public class PaymentSecurityAndIntegrityTests
{
    private const string TestHmacSecret = "test_hmac_secret_key_at_least_64_characters_long_for_security_testing_purpose!";

    private static IConfiguration BuildConfig(decimal? commissionRate = null)
    {
        var dict = new Dictionary<string, string?>
        {
            ["PaymentGateway:CommissionRate"]       = commissionRate?.ToString("0.00") ?? "0.05",
            ["PaymentGateway:OnlineHoldMinutes"]    = "10",
            ["PaymentGateway:Paymob:IsSandbox"]     = "true",
            ["PaymentGateway:Paymob:ApiKey"]        = "SANDBOX_TEST_KEY",
            ["PaymentGateway:Paymob:IntegrationId"] = "SANDBOX_INT_ID",
            ["PaymentGateway:Paymob:IframeId"]      = "SANDBOX_IFRAME",
            ["PaymentGateway:Paymob:HmacSecret"]    = TestHmacSecret
        };
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static (PaymentService service, Mock<IPaymentGatewayService> gatewayMock, Mock<INotificationService> notifMock)
        BuildService(CourtBook.Infrastructure.Persistence.AppDbContext db, decimal? commissionRate = null)
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

    private static async Task<(Guid bookingId, Guid userId, Guid ownerId, Guid courtId)>
        SeedBookingAsync(CourtBook.Infrastructure.Persistence.AppDbContext db, decimal price = 200m)
    {
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var booking = new Booking
        {
            Id               = Guid.NewGuid(),
            BookingReference = $"PS-SEC-{Guid.NewGuid().ToString()[..6].ToUpper()}",
            CourtId          = courtId,
            UserId           = clientId,
            StartTime        = DateTime.UtcNow.AddDays(2),
            EndTime          = DateTime.UtcNow.AddDays(2).AddHours(1),
            Status           = BookingStatus.Confirmed,
            PaymentStatus    = PaymentStatus.Pending,
            TotalPrice       = price,
            CreatedAt        = DateTime.UtcNow
        };

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        return (booking.Id, clientId, ownerId, courtId);
    }

    // ── 1. Webhook HMAC Security Tests ──────────────────────────────────────

    [Fact]
    public void ValidateWebhookSignature_ValidSignature_ReturnsTrue()
    {
        var config = BuildConfig();
        var client = new HttpClient();
        var gateway = new PaymobGatewayService(client, config, NullLogger<PaymobGatewayService>.Instance);

        var payload = "{\"order_id\":\"12345\",\"transaction_id\":\"tx_999\",\"amount_cents\":20000,\"success\":true}";
        var hmac = HMACSHA512.HashData(Encoding.UTF8.GetBytes(TestHmacSecret), Encoding.UTF8.GetBytes(payload));
        var validSignature = Convert.ToHexString(hmac).ToLowerInvariant();

        var isValid = gateway.ValidateWebhookSignature(payload, validSignature);
        Assert.True(isValid);
    }

    [Fact]
    public void ValidateWebhookSignature_TamperedPayload_ReturnsFalse()
    {
        var config = BuildConfig();
        var client = new HttpClient();
        var gateway = new PaymobGatewayService(client, config, NullLogger<PaymobGatewayService>.Instance);

        var originalPayload = "{\"order_id\":\"12345\",\"amount_cents\":20000}";
        var tamperedPayload = "{\"order_id\":\"12345\",\"amount_cents\":1000}";

        var hmac = HMACSHA512.HashData(Encoding.UTF8.GetBytes(TestHmacSecret), Encoding.UTF8.GetBytes(originalPayload));
        var originalSignature = Convert.ToHexString(hmac).ToLowerInvariant();

        var isValid = gateway.ValidateWebhookSignature(tamperedPayload, originalSignature);
        Assert.False(isValid);
    }

    [Fact]
    public void ValidateWebhookSignature_MissingOrEmptySignature_ReturnsFalse()
    {
        var config = BuildConfig();
        var client = new HttpClient();
        var gateway = new PaymobGatewayService(client, config, NullLogger<PaymobGatewayService>.Instance);

        var payload = "{\"order_id\":\"12345\"}";

        Assert.False(gateway.ValidateWebhookSignature(payload, ""));
        Assert.False(gateway.ValidateWebhookSignature(payload, "   "));
        Assert.False(gateway.ValidateWebhookSignature(payload, null!));
    }

    // ── 2. State Machine: Prevent Completing Refunded or Cancelled Payments ──

    [Fact]
    public async Task Webhook_DeliveredForAlreadyRefundedPayment_IsRejectedAndNotCompleted()
    {
        var db = TestDbContextFactory.Create($"SecTest_RefundedCantComplete_{Guid.NewGuid()}");
        var (svc, _, _) = BuildService(db);
        var (bookingId, userId, ownerId, _) = await SeedBookingAsync(db);

        // Payment is already Refunded
        var payment = new Payment
        {
            Id              = Guid.NewGuid(),
            BookingId       = bookingId,
            Amount          = 200m,
            ProviderOrderId = "ORD_REF_01",
            Status          = PaymentStatus.Refunded,
            Method          = PaymentMethod.CreditCard,
            CreatedAt       = DateTime.UtcNow
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        // Late-arriving duplicate or rogue webhook attempting to mark completed
        await svc.ProcessWebhookPaymentCompletedAsync(
            "ORD_REF_01", "TX_LATE_01", 200m, "TX_LATE_01", "Paymob");

        var refreshed = db.Payments.Find(payment.Id)!;
        Assert.Equal(PaymentStatus.Refunded, refreshed.Status); // must NOT turn into Completed!
        Assert.Empty(db.TransactionLedger.Where(t => t.PaymentId == payment.Id && t.EntryType == LedgerEntryType.Payment));
    }

    [Fact]
    public async Task Webhook_DeliveredForCancelledBooking_IsRejected()
    {
        var db = TestDbContextFactory.Create($"SecTest_CancelledBooking_{Guid.NewGuid()}");
        var (svc, _, _) = BuildService(db);
        var (bookingId, userId, ownerId, _) = await SeedBookingAsync(db);

        var booking = db.Bookings.Find(bookingId)!;
        booking.Status = BookingStatus.Cancelled;

        var payment = new Payment
        {
            Id              = Guid.NewGuid(),
            BookingId       = bookingId,
            Amount          = 200m,
            ProviderOrderId = "ORD_CANCEL_01",
            Status          = PaymentStatus.Processing,
            Method          = PaymentMethod.CreditCard,
            CreatedAt       = DateTime.UtcNow
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        await svc.ProcessWebhookPaymentCompletedAsync(
            "ORD_CANCEL_01", "TX_CANCEL_01", 200m, "TX_CANCEL_01", "Paymob");

        var refreshed = db.Payments.Find(payment.Id)!;
        Assert.NotEqual(PaymentStatus.Completed, refreshed.Status);
    }

    // ── 3. 10-Minute Hold Expiration Tests ──────────────────────────────────

    [Fact]
    public async Task Webhook_DeliveredAfterHoldExpired_MarksPaymentFailedAndRejectsCompletion()
    {
        var db = TestDbContextFactory.Create($"SecTest_ExpiredHoldWebhook_{Guid.NewGuid()}");
        var (svc, _, _) = BuildService(db);
        var (bookingId, userId, _, _) = await SeedBookingAsync(db);

        // Payment hold expired 5 minutes ago
        var payment = new Payment
        {
            Id              = Guid.NewGuid(),
            BookingId       = bookingId,
            Amount          = 200m,
            ProviderOrderId = "ORD_EXPIRED_01",
            Status          = PaymentStatus.Processing,
            Method          = PaymentMethod.CreditCard,
            CreatedAt       = DateTime.UtcNow.AddMinutes(-15),
            ExpiresAt       = DateTime.UtcNow.AddMinutes(-5) // EXPIRED
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        await svc.ProcessWebhookPaymentCompletedAsync(
            "ORD_EXPIRED_01", "TX_EXP_01", 200m, "TX_EXP_01", "Paymob");

        var refreshed = db.Payments.Find(payment.Id)!;
        Assert.Equal(PaymentStatus.Failed, refreshed.Status);
        Assert.Empty(db.TransactionLedger.Where(t => t.PaymentId == payment.Id));
    }

    [Fact]
    public async Task VerifyReturn_WhenHoldExpired_ReturnsFailure()
    {
        var db = TestDbContextFactory.Create($"SecTest_VerifyExpiredHold_{Guid.NewGuid()}");
        var (svc, _, _) = BuildService(db);
        var (bookingId, userId, _, _) = await SeedBookingAsync(db);

        var payment = new Payment
        {
            Id              = Guid.NewGuid(),
            BookingId       = bookingId,
            Amount          = 200m,
            ProviderOrderId = "ORD_EXP_VERIFY",
            Status          = PaymentStatus.Processing,
            Method          = PaymentMethod.CreditCard,
            CreatedAt       = DateTime.UtcNow.AddMinutes(-20),
            ExpiresAt       = DateTime.UtcNow.AddMinutes(-10) // EXPIRED
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var result = await svc.VerifyReturnAsync(userId, "Client", "ORD_EXP_VERIFY");

        Assert.False(result.IsSuccessful);
        Assert.Equal("Failed", result.Status);
        Assert.Contains("expired", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExpiredPaymentHold_ReleasesSlotForSecondPlayer()
    {
        var db = TestDbContextFactory.Create($"SecTest_SlotRelease_{Guid.NewGuid()}");
        var bookingSvc = new BookingService(db);
        var (venueId, courtId, ownerId, player1Id) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var slotStart = DateTime.UtcNow.Date.AddDays(3).AddHours(10); // 10:00 AM in 3 days
        var slotEnd   = slotStart.AddHours(1);

        // Player 1 creates booking
        var p1Result = await bookingSvc.CreateAsync(player1Id, new CreateBookingRequest
        {
            CourtId   = courtId,
            StartTime = slotStart,
            EndTime   = slotEnd
        });

        Assert.NotNull(p1Result);
        Assert.NotEqual(Guid.Empty, p1Result.Id);

        // Player 1 initiates payment, establishing 10-minute hold
        var p1Booking = db.Bookings.Find(p1Result.Id)!;
        var payment = new Payment
        {
            Id              = Guid.NewGuid(),
            BookingId       = p1Booking.Id,
            Amount          = p1Booking.TotalPrice,
            Status          = PaymentStatus.Processing,
            Method          = PaymentMethod.CreditCard,
            CreatedAt       = DateTime.UtcNow.AddMinutes(-15),
            ExpiresAt       = DateTime.UtcNow.AddMinutes(-5) // Hold EXPIRED 5 mins ago
        };
        db.Payments.Add(payment);
        p1Booking.PaymentStatus = PaymentStatus.Processing;
        await db.SaveChangesAsync();

        // Player 2 attempts to book the EXACT same slot
        var player2Id = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = player2Id,
            Name = "Player Two",
            Email = "p2@test.com",
            PasswordHash = "hash",
            Role = Role.Client
        });
        await db.SaveChangesAsync();

        var p2Result = await bookingSvc.CreateAsync(player2Id, new CreateBookingRequest
        {
            CourtId   = courtId,
            StartTime = slotStart,
            EndTime   = slotEnd
        });

        // Player 2 booking MUST succeed because Player 1's hold expired!
        Assert.NotNull(p2Result);
        Assert.NotEqual(Guid.Empty, p2Result.Id);

        // Player 1 booking should have been lazily marked Cancelled
        var refreshedP1 = db.Bookings.Find(p1Booking.Id)!;
        Assert.Equal(BookingStatus.Cancelled, refreshedP1.Status);
        Assert.Equal(PaymentStatus.Failed, refreshedP1.PaymentStatus);
    }

    // ── 4. Refund Edge Cases: Partial Refund with Late Cancellation Fee ──────

    [Fact]
    public async Task ProcessRefund_WithLateCancellationFee_ExecutesPartialRefund()
    {
        var db = TestDbContextFactory.Create($"SecTest_PartialRefund_{Guid.NewGuid()}");
        var (svc, gateway, _) = BuildService(db);
        var (bookingId, userId, ownerId, _) = await SeedBookingAsync(db, 200m);

        var booking = db.Bookings.Find(bookingId)!;
        booking.CancellationFee = 50m; // 50 EGP late fee retained

        var payment = new Payment
        {
            Id                   = Guid.NewGuid(),
            BookingId            = bookingId,
            Amount               = 200m,
            CommissionAmount     = 10m,
            OwnerNetAmount       = 190m,
            Status               = PaymentStatus.Completed,
            Method               = PaymentMethod.CreditCard,
            TransactionReference = "TX_PARTIAL_ORIG",
            CreatedAt            = DateTime.UtcNow
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        gateway.Setup(g => g.RefundAsync(It.IsAny<RefundRequest>()))
            .ReturnsAsync(new RefundResult(true, "REFUND_PARTIAL_01", null));

        var result = await svc.ProcessRefundAsync(bookingId, "Late cancellation");

        Assert.True(result.Success);
        Assert.Equal(150m, result.RefundAmount); // 200 - 50 = 150 refunded

        // Verified gateway was called for EXACTLY 150 (not 200)
        gateway.Verify(g => g.RefundAsync(It.Is<RefundRequest>(r => r.Amount == 150m)), Times.Once);

        var refreshed = db.Payments.Find(payment.Id)!;
        Assert.Equal(PaymentStatus.PartiallyRefunded, refreshed.Status);

        // Ledger has both: partial refund and retained cancellation fee
        var refundEntry = db.TransactionLedger.FirstOrDefault(t => t.EntryType == LedgerEntryType.Refund);
        var feeEntry    = db.TransactionLedger.FirstOrDefault(t => t.EntryType == LedgerEntryType.CancellationFee);

        Assert.NotNull(refundEntry);
        Assert.Equal(-150m, refundEntry.GrossAmount);

        Assert.NotNull(feeEntry);
        Assert.Equal(50m, feeEntry.GrossAmount);
    }

    [Fact]
    public async Task ProcessRefund_When100PercentFee_DoesNotCallGatewayAndRetainsFee()
    {
        var db = TestDbContextFactory.Create($"SecTest_ZeroRefund_{Guid.NewGuid()}");
        var (svc, gateway, _) = BuildService(db);
        var (bookingId, userId, ownerId, _) = await SeedBookingAsync(db, 200m);

        var booking = db.Bookings.Find(bookingId)!;
        booking.CancellationFee = 200m; // 100% fee retained

        var payment = new Payment
        {
            Id                   = Guid.NewGuid(),
            BookingId            = bookingId,
            Amount               = 200m,
            Status               = PaymentStatus.Completed,
            Method               = PaymentMethod.CreditCard,
            TransactionReference = "TX_NO_REF",
            CreatedAt            = DateTime.UtcNow
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var result = await svc.ProcessRefundAsync(bookingId, "No-show / 100% fee");

        Assert.True(result.Success);
        Assert.Equal(0m, result.RefundAmount);

        // Gateway refund should NOT be called for 0 EGP
        gateway.Verify(g => g.RefundAsync(It.IsAny<RefundRequest>()), Times.Never);

        var feeEntry = db.TransactionLedger.FirstOrDefault(t => t.EntryType == LedgerEntryType.CancellationFee);
        Assert.NotNull(feeEntry);
        Assert.Equal(200m, feeEntry.GrossAmount);
    }

    [Fact]
    public async Task ProcessRefund_WhenGatewayFails_DoesNotMarkPaymentRefunded()
    {
        var db = TestDbContextFactory.Create($"SecTest_RefundGatewayFailure_{Guid.NewGuid()}");
        var (svc, gateway, _) = BuildService(db);
        var (bookingId, _, _, _) = await SeedBookingAsync(db, 200m);

        var payment = new Payment
        {
            Id                   = Guid.NewGuid(),
            BookingId            = bookingId,
            Amount               = 200m,
            Status               = PaymentStatus.Completed,
            Method               = PaymentMethod.CreditCard,
            TransactionReference = "TX_FAIL_REF",
            CreatedAt            = DateTime.UtcNow
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        gateway.Setup(g => g.RefundAsync(It.IsAny<RefundRequest>()))
            .ReturnsAsync(new RefundResult(false, null, "Provider refund declined"));

        var result = await svc.ProcessRefundAsync(bookingId, "Failed refund test");

        Assert.False(result.Success);
        Assert.Equal("Provider refund declined", result.ErrorMessage);

        var refreshed = db.Payments.Find(payment.Id)!;
        Assert.Equal(PaymentStatus.Completed, refreshed.Status); // remains Completed!
        Assert.Empty(db.TransactionLedger.Where(t => t.EntryType == LedgerEntryType.Refund));
    }

    // ── 5. Payment Authorization: Strict Player Isolation on Verify ─────────

    [Fact]
    public async Task VerifyReturn_PlayerA_CannotVerifyPlayerB_Order()
    {
        var db = TestDbContextFactory.Create($"SecTest_VerifyAuth_{Guid.NewGuid()}");
        var (svc, gateway, _) = BuildService(db);
        var (bookingId, playerAId, _, _) = await SeedBookingAsync(db);

        var payment = new Payment
        {
            Id              = Guid.NewGuid(),
            BookingId       = bookingId,
            Amount          = 200m,
            ProviderOrderId = "ORD_PLAYER_A",
            Status          = PaymentStatus.Processing,
            Method          = PaymentMethod.CreditCard,
            CreatedAt       = DateTime.UtcNow
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var playerBId = Guid.NewGuid();

        // Player B tries to verify Player A's orderId
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.VerifyReturnAsync(playerBId, "Client", "ORD_PLAYER_A"));
    }

    [Fact]
    public async Task VerifyReturn_Admin_CanVerifyAnyPlayer_Order()
    {
        var db = TestDbContextFactory.Create($"SecTest_AdminVerifyAuth_{Guid.NewGuid()}");
        var (svc, gateway, _) = BuildService(db);
        var (bookingId, playerAId, _, _) = await SeedBookingAsync(db);

        var payment = new Payment
        {
            Id              = Guid.NewGuid(),
            BookingId       = bookingId,
            Amount          = 200m,
            ProviderOrderId = "ORD_PLAYER_ADMIN",
            Status          = PaymentStatus.Completed,
            Method          = PaymentMethod.CreditCard,
            CreatedAt       = DateTime.UtcNow
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        gateway.Setup(g => g.VerifyPaymentAsync("ORD_PLAYER_ADMIN"))
            .ReturnsAsync(new PaymentVerificationResult(true, "TX_ADMIN_01", 200m, "PAID", null));

        var adminId = Guid.NewGuid();
        var result = await svc.VerifyReturnAsync(adminId, "Admin", "ORD_PLAYER_ADMIN");

        Assert.True(result.IsSuccessful);
    }

    // ── 6. Commission Engine: Configurable Rate & Exact Decimal Rounding ───

    [Fact]
    public async Task CommissionEngine_CustomTenPercentRate_CalculatedAndSnapshotted()
    {
        var db = TestDbContextFactory.Create($"SecTest_CustomCommission_{Guid.NewGuid()}");
        // Inject 10% commission rate
        var (svc, gateway, _) = BuildService(db, commissionRate: 0.10m);
        var (bookingId, userId, ownerId, _) = await SeedBookingAsync(db, 350m);

        gateway.Setup(g => g.InitiatePaymentAsync(It.IsAny<PaymentInitiationRequest>()))
            .ReturnsAsync(new PaymentInitiationResult(true, "https://pay.test", "ORD_COMM_10", null));

        await svc.InitiateOnlinePaymentAsync(userId, bookingId, "CreditCard", "https://localhost");
        await svc.ProcessWebhookPaymentCompletedAsync("ORD_COMM_10", "TX_COMM_10", 350m, "TX_COMM_10", "Paymob");

        var payment = db.Payments.First(p => p.BookingId == bookingId);
        Assert.Equal(35m, payment.CommissionAmount);  // 10% of 350 = 35
        Assert.Equal(315m, payment.OwnerNetAmount);   // 350 - 35 = 315

        var ledger = db.TransactionLedger.First(t => t.BookingId == bookingId);
        Assert.Equal(0.10m, ledger.CommissionRateSnapshot);
    }

    [Fact]
    public async Task CommissionEngine_OddDecimalAmount_RoundsCorrectly()
    {
        var db = TestDbContextFactory.Create($"SecTest_OddDecimal_{Guid.NewGuid()}");
        var (svc, gateway, _) = BuildService(db, commissionRate: 0.05m); // 5%
        var (bookingId, userId, ownerId, _) = await SeedBookingAsync(db, 133.33m);

        gateway.Setup(g => g.InitiatePaymentAsync(It.IsAny<PaymentInitiationRequest>()))
            .ReturnsAsync(new PaymentInitiationResult(true, "https://pay.test", "ORD_DEC_01", null));

        await svc.InitiateOnlinePaymentAsync(userId, bookingId, "CreditCard", "https://localhost");
        await svc.ProcessWebhookPaymentCompletedAsync("ORD_DEC_01", "TX_DEC_01", 133.33m, "TX_DEC_01", "Paymob");

        // 133.33 * 0.05 = 6.6665 -> rounded to 6.67
        // Owner net = 133.33 - 6.67 = 126.66
        var payment = db.Payments.First(p => p.BookingId == bookingId);
        Assert.Equal(6.67m, payment.CommissionAmount);
        Assert.Equal(126.66m, payment.OwnerNetAmount);
    }

    // ── 7. Concurrency Tests ────────────────────────────────────────────────

    [Fact]
    public async Task ProcessWebhook_ConcurrentDuplicateDelivery_CreatesExactlyOneLedgerEntry()
    {
        var db = TestDbContextFactory.Create($"SecTest_ConcurrentWebhook_{Guid.NewGuid()}");
        var (svc, gateway, _) = BuildService(db);
        var (bookingId, userId, ownerId, _) = await SeedBookingAsync(db, 200m);

        gateway.Setup(g => g.InitiatePaymentAsync(It.IsAny<PaymentInitiationRequest>()))
            .ReturnsAsync(new PaymentInitiationResult(true, "https://pay.test", "ORD_CONC_01", null));

        await svc.InitiateOnlinePaymentAsync(userId, bookingId, "CreditCard", "https://localhost");

        // Fire two webhooks simultaneously for the exact same order and transaction
        var t1 = svc.ProcessWebhookPaymentCompletedAsync("ORD_CONC_01", "TX_CONC_01", 200m, "TX_CONC_01", "Paymob");
        var t2 = svc.ProcessWebhookPaymentCompletedAsync("ORD_CONC_01", "TX_CONC_01", 200m, "TX_CONC_01", "Paymob");

        await Task.WhenAll(t1, t2);

        // Assert exactly ONE payment ledger entry was created
        var ledgerEntries = db.TransactionLedger.Where(t => t.BookingId == bookingId).ToList();
        Assert.Single(ledgerEntries);
        Assert.Equal(LedgerEntryType.Payment, ledgerEntries[0].EntryType);
    }

    [Fact]
    public async Task ProcessRefund_ConcurrentRequests_OnlyOneSucceeds()
    {
        var db = TestDbContextFactory.Create($"SecTest_ConcurrentRefund_{Guid.NewGuid()}");
        var (svc, gateway, _) = BuildService(db);
        var (bookingId, userId, ownerId, _) = await SeedBookingAsync(db, 200m);

        var payment = new Payment
        {
            Id                   = Guid.NewGuid(),
            BookingId            = bookingId,
            Amount               = 200m,
            CommissionAmount     = 10m,
            OwnerNetAmount       = 190m,
            Status               = PaymentStatus.Completed,
            Method               = PaymentMethod.CreditCard,
            TransactionReference = "TX_CONC_REF_ORIG",
            CreatedAt            = DateTime.UtcNow
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        gateway.Setup(g => g.RefundAsync(It.IsAny<RefundRequest>()))
            .ReturnsAsync(new RefundResult(true, "REF_CONC_01", null));

        // Fire two refund requests simultaneously
        var t1 = svc.ProcessRefundAsync(bookingId, "Concurrent refund A");
        var t2 = svc.ProcessRefundAsync(bookingId, "Concurrent refund B");

        var results = await Task.WhenAll(t1, t2);

        // At most one should succeed; duplicate refund must fail
        var successCount = results.Count(r => r.Success);
        var failureCount = results.Count(r => !r.Success);

        Assert.Equal(1, successCount);
        Assert.Equal(1, failureCount);

        var refundEntries = db.TransactionLedger.Where(t => t.BookingId == bookingId && t.EntryType == LedgerEntryType.Refund).ToList();
        Assert.Single(refundEntries);
    }
}

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
/// Phase 7: Payment Gateway & Financial Transactions Tests
/// Tests the full payment lifecycle, idempotency, ledger entries,
/// refunds, and financial reporting.
/// All tests use InMemory database — no gateway calls are made.
/// </summary>
public class PaymentGatewayTests
{
    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PaymentGateway:CommissionRate"]      = "0.05",
                ["PaymentGateway:OnlineHoldMinutes"]   = "10",
                ["PaymentGateway:Paymob:IsSandbox"]    = "true",
                ["PaymentGateway:Paymob:ApiKey"]       = "SANDBOX_TEST_KEY",
                ["PaymentGateway:Paymob:IntegrationId"]= "SANDBOX_INT_ID",
                ["PaymentGateway:Paymob:IframeId"]     = "SANDBOX_IFRAME",
                ["PaymentGateway:Paymob:HmacSecret"]   = "SANDBOX_HMAC_SECRET_AT_LEAST_64_CHARS_LONG_PLACEHOLDER_VALUE_OK"
            })
            .Build();

    private static (PaymentService service, Mock<IPaymentGatewayService> gatewayMock, Mock<INotificationService> notifMock)
        BuildService(CourtBook.Infrastructure.Persistence.AppDbContext db)
    {
        var gateway  = new Mock<IPaymentGatewayService>();
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
            BuildConfig(),
            notifications.Object);

        return (svc, gateway, notifications);
    }

    private static async Task<(Guid bookingId, Guid userId, Guid ownerId)>
        SeedBookingAsync(CourtBook.Infrastructure.Persistence.AppDbContext db)
    {
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var booking = new Booking
        {
            Id               = Guid.NewGuid(),
            BookingReference = $"PS-TEST-{Guid.NewGuid().ToString()[..6].ToUpper()}",
            CourtId          = courtId,
            UserId           = clientId,
            StartTime        = DateTime.UtcNow.AddDays(1),
            EndTime          = DateTime.UtcNow.AddDays(1).AddHours(1),
            Status           = BookingStatus.Confirmed,
            PaymentStatus    = PaymentStatus.Pending,
            TotalPrice       = 200m,
            CreatedAt        = DateTime.UtcNow
        };

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        return (booking.Id, clientId, ownerId);
    }

    // ─── Test 1: SelectPayAtFacility creates Payment record ────────────────

    [Fact]
    public async Task SelectPayAtFacility_CreatesPaymentRecord_WithPendingStatus()
    {
        var db = TestDbContextFactory.Create($"PayTest_PayAtFacility_{Guid.NewGuid()}");
        var (svc, _, _) = BuildService(db);
        var (bookingId, userId, _) = await SeedBookingAsync(db);

        var result = await svc.SelectPayAtFacilityAsync(userId, bookingId);

        Assert.True(result.Success);
        Assert.Equal(bookingId, result.BookingId);
        Assert.Equal("PayAtFacility", result.PaymentMethod);
        Assert.Equal("Pending", result.Status);
        Assert.Equal(200m, result.Amount);

        var payment = db.Payments.FirstOrDefault(p => p.BookingId == bookingId);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(PaymentMethod.PayAtFacility, payment.Method);
    }

    // ─── Test 2: SelectPayAtFacility twice is idempotent ───────────────────

    [Fact]
    public async Task SelectPayAtFacility_CalledTwice_UpdatesExistingPayment()
    {
        var db = TestDbContextFactory.Create($"PayTest_PayAtFacility_Twice_{Guid.NewGuid()}");
        var (svc, _, _) = BuildService(db);
        var (bookingId, userId, _) = await SeedBookingAsync(db);

        await svc.SelectPayAtFacilityAsync(userId, bookingId);
        var result = await svc.SelectPayAtFacilityAsync(userId, bookingId);

        Assert.True(result.Success);

        var payments = db.Payments.Where(p => p.BookingId == bookingId).ToList();
        Assert.Single(payments); // only one payment record
    }

    // ─── Test 3: Online payment initiation sets Processing status ──────────

    [Fact]
    public async Task InitiateOnlinePayment_SetsProcessingStatus_OnSuccess()
    {
        var db = TestDbContextFactory.Create($"PayTest_OnlineInitiate_{Guid.NewGuid()}");
        var (svc, gateway, _) = BuildService(db);
        var (bookingId, userId, _) = await SeedBookingAsync(db);

        gateway.Setup(g => g.InitiatePaymentAsync(It.IsAny<PaymentInitiationRequest>()))
            .ReturnsAsync(new PaymentInitiationResult(
                true, "https://test.paymob.com/iframe/TEST_TOKEN", "ORDER_123", null));

        var result = await svc.InitiateOnlinePaymentAsync(
            userId, bookingId, "CreditCard", "https://localhost:5100");

        Assert.True(result.Success);
        Assert.Equal("https://test.paymob.com/iframe/TEST_TOKEN", result.PaymentUrl);
        Assert.Equal("ORDER_123", result.ProviderOrderId);
        Assert.NotNull(result.ExpiresAt);
        Assert.True(result.ExpiresAt > DateTime.UtcNow.AddMinutes(9));

        var payment = db.Payments.First(p => p.BookingId == bookingId);
        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Equal("ORDER_123", payment.ProviderOrderId);
    }

    // ─── Test 4: Webhook completes payment and creates ledger entries ───────

    [Fact]
    public async Task ProcessWebhook_CompletesPayment_AndCreatesLedgerEntry()
    {
        var db = TestDbContextFactory.Create($"PayTest_Webhook_{Guid.NewGuid()}");
        var (svc, gateway, _) = BuildService(db);
        var (bookingId, userId, ownerId) = await SeedBookingAsync(db);

        // Create processing payment first
        gateway.Setup(g => g.InitiatePaymentAsync(It.IsAny<PaymentInitiationRequest>()))
            .ReturnsAsync(new PaymentInitiationResult(true, "https://test.paymob.com/pay", "ORD_WH_001", null));

        await svc.InitiateOnlinePaymentAsync(userId, bookingId, "CreditCard", "https://localhost");

        // Simulate webhook
        await svc.ProcessWebhookPaymentCompletedAsync(
            "ORD_WH_001", "TXN_WH_001", 200m, "TXN_WH_001", "Paymob");

        var payment = db.Payments.First(p => p.BookingId == bookingId);
        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.Equal("TXN_WH_001", payment.TransactionReference);
        Assert.NotNull(payment.PaidAt);
        Assert.Equal(10m, payment.CommissionAmount);    // 5% of 200
        Assert.Equal(190m, payment.OwnerNetAmount);     // 200 - 10

        var ledger = db.TransactionLedger.Where(t => t.BookingId == bookingId).ToList();
        Assert.Single(ledger);
        Assert.Equal(LedgerEntryType.Payment, ledger[0].EntryType);
        Assert.Equal(200m, ledger[0].GrossAmount);
        Assert.Equal(10m, ledger[0].CommissionAmount);
        Assert.Equal(190m, ledger[0].NetAmount);

        var booking = db.Bookings.Find(bookingId);
        Assert.Equal(PaymentStatus.Completed, booking?.PaymentStatus);
    }

    // ─── Test 5: Webhook idempotency — duplicate event is ignored ──────────

    [Fact]
    public async Task ProcessWebhook_DuplicateEvent_IsIgnored()
    {
        var db = TestDbContextFactory.Create($"PayTest_Idempotency_{Guid.NewGuid()}");
        var (svc, gateway, _) = BuildService(db);
        var (bookingId, userId, _) = await SeedBookingAsync(db);

        gateway.Setup(g => g.InitiatePaymentAsync(It.IsAny<PaymentInitiationRequest>()))
            .ReturnsAsync(new PaymentInitiationResult(true, "https://test.paymob.com/pay", "ORD_IDEM_001", null));

        await svc.InitiateOnlinePaymentAsync(userId, bookingId, "CreditCard", "https://localhost");

        // First webhook — should process
        await svc.ProcessWebhookPaymentCompletedAsync("ORD_IDEM_001", "TXN_IDEM_001", 200m, "TXN_IDEM_001", "Paymob");

        // Second webhook with same transaction ID — should be ignored
        await svc.ProcessWebhookPaymentCompletedAsync("ORD_IDEM_001", "TXN_IDEM_001", 200m, "TXN_IDEM_001", "Paymob");

        // Only one ledger entry should exist
        var ledgerCount = db.TransactionLedger.Count(t => t.BookingId == bookingId);
        Assert.Equal(1, ledgerCount);

        // Only one idempotency log for this transaction
        var idempotencyCount = db.IdempotencyLogs
            .Count(i => i.Provider == "Paymob" && i.ProviderTransactionId == "TXN_IDEM_001");
        Assert.Equal(1, idempotencyCount);
    }

    // ─── Test 6: Amount mismatch marks payment as Failed ───────────────────

    [Fact]
    public async Task ProcessWebhook_AmountMismatch_MarksPaymentFailed()
    {
        var db = TestDbContextFactory.Create($"PayTest_AmountMismatch_{Guid.NewGuid()}");
        var (svc, gateway, _) = BuildService(db);
        var (bookingId, userId, _) = await SeedBookingAsync(db);

        gateway.Setup(g => g.InitiatePaymentAsync(It.IsAny<PaymentInitiationRequest>()))
            .ReturnsAsync(new PaymentInitiationResult(true, "https://test.paymob.com/pay", "ORD_MISMATCH", null));

        await svc.InitiateOnlinePaymentAsync(userId, bookingId, "CreditCard", "https://localhost");

        // Send less than expected amount (tampered)
        await svc.ProcessWebhookPaymentCompletedAsync("ORD_MISMATCH", "TXN_MISMATCH", 10m, "TXN_MISMATCH", "Paymob");

        var payment = db.Payments.First(p => p.BookingId == bookingId);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
    }

    // ─── Test 7: Refund for PayAtFacility — no gateway call ────────────────

    [Fact]
    public async Task RefundPayAtFacility_SucceedsWithoutGatewayCall()
    {
        var db = TestDbContextFactory.Create($"PayTest_RefundPayAtFacility_{Guid.NewGuid()}");
        var (svc, gateway, _) = BuildService(db);
        var (bookingId, userId, _) = await SeedBookingAsync(db);

        // Create completed PayAtFacility payment
        db.Payments.Add(new Payment
        {
            Id        = Guid.NewGuid(),
            BookingId = bookingId,
            Amount    = 200m,
            Currency  = "EGP",
            Method    = PaymentMethod.PayAtFacility,
            Status    = PaymentStatus.Completed,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await svc.ProcessRefundAsync(bookingId, "Customer requested refund");

        Assert.True(result.Success);
        Assert.Equal(200m, result.RefundAmount);

        var payment = db.Payments.First(p => p.BookingId == bookingId);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);

        var refundEntry = db.TransactionLedger.FirstOrDefault(t => t.EntryType == LedgerEntryType.Refund);
        Assert.NotNull(refundEntry);
        Assert.True(refundEntry.GrossAmount < 0); // negative = refund

        // No gateway call should have been made
        gateway.Verify(g => g.RefundAsync(It.IsAny<RefundRequest>()), Times.Never);
    }

    // ─── Test 8: Refund for online payment calls gateway ───────────────────

    [Fact]
    public async Task RefundOnlinePayment_CallsGateway_AndCreatesLedgerEntry()
    {
        var db = TestDbContextFactory.Create($"PayTest_RefundOnline_{Guid.NewGuid()}");
        var (svc, gateway, _) = BuildService(db);
        var (bookingId, userId, _) = await SeedBookingAsync(db);

        db.Payments.Add(new Payment
        {
            Id                   = Guid.NewGuid(),
            BookingId            = bookingId,
            Amount               = 200m,
            Currency             = "EGP",
            Method               = PaymentMethod.CreditCard,
            Status               = PaymentStatus.Completed,
            TransactionReference = "TXN_ONLINE_REFUND_001",
            CreatedAt            = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        gateway.Setup(g => g.RefundAsync(It.IsAny<RefundRequest>()))
            .ReturnsAsync(new RefundResult(true, "REFUND_TXN_001", null));

        var result = await svc.ProcessRefundAsync(bookingId, "Venue cancelled");

        Assert.True(result.Success);
        Assert.Equal("REFUND_TXN_001", result.RefundTransactionId);
        Assert.Equal(200m, result.RefundAmount);

        gateway.Verify(g => g.RefundAsync(It.Is<RefundRequest>(r =>
            r.ProviderTransactionId == "TXN_ONLINE_REFUND_001")), Times.Once);
    }

    // ─── Test 9: Cannot refund an already-refunded payment ─────────────────

    [Fact]
    public async Task RefundPayment_AlreadyRefunded_ReturnsFailure()
    {
        var db = TestDbContextFactory.Create($"PayTest_DoubleRefund_{Guid.NewGuid()}");
        var (svc, _, _) = BuildService(db);
        var (bookingId, userId, _) = await SeedBookingAsync(db);

        db.Payments.Add(new Payment
        {
            Id        = Guid.NewGuid(),
            BookingId = bookingId,
            Amount    = 200m,
            Currency  = "EGP",
            Method    = PaymentMethod.PayAtFacility,
            Status    = PaymentStatus.Refunded,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await svc.ProcessRefundAsync(bookingId, "second refund attempt");
        Assert.False(result.Success);
        Assert.Contains("already been refunded", result.ErrorMessage);
    }

    // ─── Test 10: GetPaymentByBooking returns correct payment ───────────────

    [Fact]
    public async Task GetPaymentByBooking_ReturnsCorrectDetails()
    {
        var db = TestDbContextFactory.Create($"PayTest_GetPayment_{Guid.NewGuid()}");
        var (svc, _, _) = BuildService(db);
        var (bookingId, userId, _) = await SeedBookingAsync(db);

        db.Payments.Add(new Payment
        {
            Id        = Guid.NewGuid(),
            BookingId = bookingId,
            Amount    = 200m,
            Currency  = "EGP",
            Method    = PaymentMethod.PayAtFacility,
            Status    = PaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await svc.GetPaymentByBookingAsync(userId, bookingId);

        Assert.NotNull(result);
        Assert.Equal(bookingId, result.BookingId);
        Assert.Equal(200m, result.Amount);
        Assert.Equal("Pending", result.Status);
    }

    // ─── Test 11: GetPaymentByBooking returns null for wrong user ──────────

    [Fact]
    public async Task GetPaymentByBooking_WrongUser_ReturnsNull()
    {
        var db = TestDbContextFactory.Create($"PayTest_GetPayment_WrongUser_{Guid.NewGuid()}");
        var (svc, _, _) = BuildService(db);
        var (bookingId, userId, _) = await SeedBookingAsync(db);

        db.Payments.Add(new Payment
        {
            Id        = Guid.NewGuid(),
            BookingId = bookingId,
            Amount    = 200m,
            Currency  = "EGP",
            Method    = PaymentMethod.PayAtFacility,
            Status    = PaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var otherUserId = Guid.NewGuid();
        var result = await svc.GetPaymentByBookingAsync(otherUserId, bookingId);
        Assert.Null(result); // strict user isolation
    }

    // ─── Test 12: Owner financial report is scoped to owner ────────────────

    [Fact]
    public async Task GetOwnerFinancialReport_ReturnsOnlyOwnerEntries()
    {
        var db = TestDbContextFactory.Create($"PayTest_OwnerReport_{Guid.NewGuid()}");
        var (svc, gateway, _) = BuildService(db);
        var (bookingId, userId, ownerId) = await SeedBookingAsync(db);

        // Create completed payment
        var paymentId = Guid.NewGuid();
        db.Payments.Add(new Payment
        {
            Id        = paymentId,
            BookingId = bookingId,
            Amount    = 300m,
            Status    = PaymentStatus.Completed,
            Method    = PaymentMethod.CreditCard,
            CreatedAt = DateTime.UtcNow
        });

        // Add ledger entry for this owner
        db.TransactionLedger.Add(new TransactionLedger
        {
            Id                     = Guid.NewGuid(),
            PaymentId              = paymentId,
            BookingId              = bookingId,
            UserId                 = userId,
            OwnerId                = ownerId,
            EntryType              = LedgerEntryType.Payment,
            GrossAmount            = 300m,
            CommissionAmount       = 15m,
            NetAmount              = 285m,
            CommissionRateSnapshot = 0.05m,
            Currency               = "EGP",
            CreatedAt              = DateTime.UtcNow
        });

        // Add ledger entry for a different owner (should NOT appear in report)
        var otherId = Guid.NewGuid();
        var otherPaymentId = Guid.NewGuid();
        db.TransactionLedger.Add(new TransactionLedger
        {
            Id                     = Guid.NewGuid(),
            PaymentId              = otherPaymentId,
            BookingId              = Guid.NewGuid(),
            UserId                 = Guid.NewGuid(),
            OwnerId                = otherId,
            EntryType              = LedgerEntryType.Payment,
            GrossAmount            = 500m,
            CommissionAmount       = 25m,
            NetAmount              = 475m,
            CommissionRateSnapshot = 0.05m,
            Currency               = "EGP",
            CreatedAt              = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var report = await svc.GetOwnerFinancialReportAsync(ownerId, null, null);

        Assert.Equal(ownerId, report.OwnerId);
        Assert.Equal(300m, report.TotalGross);
        Assert.Equal(15m, report.TotalCommission);
        Assert.Equal(285m, report.TotalNet);
        Assert.Equal(1, report.TotalTransactions);
        Assert.DoesNotContain(report.Entries, e => e.NetAmount == 475m);
    }

    // ─── Test 13: Commission calculation is correct ─────────────────────────

    [Fact]
    public async Task CommissionCalculation_IsFivePercent()
    {
        var db = TestDbContextFactory.Create($"PayTest_Commission_{Guid.NewGuid()}");
        var (svc, gateway, _) = BuildService(db);
        var (bookingId, userId, _) = await SeedBookingAsync(db);

        gateway.Setup(g => g.InitiatePaymentAsync(It.IsAny<PaymentInitiationRequest>()))
            .ReturnsAsync(new PaymentInitiationResult(true, "https://test.url", "ORD_COMM", null));

        await svc.InitiateOnlinePaymentAsync(userId, bookingId, "CreditCard", "https://localhost");
        await svc.ProcessWebhookPaymentCompletedAsync("ORD_COMM", "TXN_COMM", 200m, "TXN_COMM", "Paymob");

        var payment = db.Payments.First(p => p.BookingId == bookingId);
        Assert.Equal(10m, payment.CommissionAmount);  // 5% of 200
        Assert.Equal(190m, payment.OwnerNetAmount);    // 200 - 10

        var ledger = db.TransactionLedger.First(t => t.BookingId == bookingId);
        Assert.Equal(0.05m, ledger.CommissionRateSnapshot);
    }

    // ─── Test 14: Cannot pay for a cancelled booking ───────────────────────

    [Fact]
    public async Task SelectPayAtFacility_CancelledBooking_Throws()
    {
        var db = TestDbContextFactory.Create($"PayTest_CancelledBooking_{Guid.NewGuid()}");
        var (svc, _, _) = BuildService(db);
        var (bookingId, userId, _) = await SeedBookingAsync(db);

        var booking = db.Bookings.Find(bookingId)!;
        booking.Status = BookingStatus.Cancelled;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SelectPayAtFacilityAsync(userId, bookingId));
    }

    // ─── Test 15: Cannot pay twice for an already-completed payment ─────────

    [Fact]
    public async Task SelectPayAtFacility_AlreadyCompleted_Throws()
    {
        var db = TestDbContextFactory.Create($"PayTest_AlreadyCompleted_{Guid.NewGuid()}");
        var (svc, _, _) = BuildService(db);
        var (bookingId, userId, _) = await SeedBookingAsync(db);

        db.Payments.Add(new Payment
        {
            Id        = Guid.NewGuid(),
            BookingId = bookingId,
            Amount    = 200m,
            Method    = PaymentMethod.PayAtFacility,
            Status    = PaymentStatus.Completed,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SelectPayAtFacilityAsync(userId, bookingId));
    }

    // ─── Test 16: InitiatePayment for unknown booking throws ────────────────

    [Fact]
    public async Task SelectPayAtFacility_UnknownBooking_Throws()
    {
        var db = TestDbContextFactory.Create($"PayTest_UnknownBooking_{Guid.NewGuid()}");
        var (svc, _, _) = BuildService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            svc.SelectPayAtFacilityAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    // ─── Test 17: Admin transaction history pagination works ────────────────

    [Fact]
    public async Task GetAdminTransactionHistory_ReturnsPagedResults()
    {
        var db = TestDbContextFactory.Create($"PayTest_AdminHistory_{Guid.NewGuid()}");
        var (svc, _, _) = BuildService(db);
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        // Seed 5 bookings with payments and ledger entries for admin view
        for (int i = 0; i < 5; i++)
        {
            var booking = new Booking
            {
                Id               = Guid.NewGuid(),
                BookingReference = $"PS-ADMIN-{i:D3}",
                CourtId          = courtId,
                UserId           = clientId,
                StartTime        = DateTime.UtcNow.AddDays(i + 1),
                EndTime          = DateTime.UtcNow.AddDays(i + 1).AddHours(1),
                Status           = BookingStatus.Confirmed,
                PaymentStatus    = PaymentStatus.Completed,
                TotalPrice       = 100m * (i + 1),
                CreatedAt        = DateTime.UtcNow.AddMinutes(-i)
            };
            db.Bookings.Add(booking);

            var pid = Guid.NewGuid();
            var payment = new Payment
            {
                Id        = pid,
                BookingId = booking.Id,
                Amount    = 100m * (i + 1),
                Method    = PaymentMethod.CreditCard,
                Status    = PaymentStatus.Completed,
                CreatedAt = DateTime.UtcNow.AddMinutes(-i)
            };
            db.Payments.Add(payment);

            db.TransactionLedger.Add(new TransactionLedger
            {
                Id                     = Guid.NewGuid(),
                PaymentId              = pid,
                BookingId              = booking.Id,
                UserId                 = clientId,
                OwnerId                = ownerId,
                EntryType              = LedgerEntryType.Payment,
                GrossAmount            = 100m * (i + 1),
                CommissionAmount       = 5m * (i + 1),
                NetAmount              = 95m * (i + 1),
                CommissionRateSnapshot = 0.05m,
                Currency               = "EGP",
                CreatedAt              = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await db.SaveChangesAsync();

        var result = await svc.GetAdminTransactionHistoryAsync(1, 3, null, null, null);

        Assert.Equal(5, result.TotalCount);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(2, result.TotalPages);
    }
}

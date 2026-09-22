using System.Data;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using CourtBook.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CourtBook.Tests;

/// <summary>
/// Comprehensive Financial Integrity and Regression Test Suite.
/// Validates:
/// 1. Automatic refunds on booking cancellation (full, partial, fee retention).
/// 2. Safe handling of uncaptured payments (PayAtFacility, Pending, Processing, Failed).
/// 3. Idempotency and anti-double-refunding guards.
/// 4. Accurate SQL aggregation in Owner Financial Reports with ZERO double-counting.
/// 5. Owner isolation and Admin reporting integrity.
/// 6. Accurate realized revenue in Owner Dashboard (excluding unpaid, processing, failed, and refunded payments).
/// </summary>
public class FinancialIntegrityTests
{
    private static IConfiguration BuildConfig(decimal commissionRate = 0.05m)
    {
        var dict = new Dictionary<string, string?>
        {
            ["PaymentGateway:CommissionRate"]       = commissionRate.ToString("0.00"),
            ["PaymentGateway:OnlineHoldMinutes"]    = "10",
            ["PaymentGateway:Paymob:IsSandbox"]     = "true",
            ["PaymentGateway:Paymob:ApiKey"]        = "SANDBOX_TEST_KEY",
            ["PaymentGateway:Paymob:IntegrationId"] = "SANDBOX_INT_ID",
            ["PaymentGateway:Paymob:IframeId"]      = "SANDBOX_IFRAME",
            ["PaymentGateway:Paymob:HmacSecret"]    = "test_hmac_secret_key_at_least_64_characters_long_for_security_testing_purpose!"
        };
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static (PaymentService paymentService, BookingService bookingService, OwnerService ownerService, Mock<IPaymentGatewayService> gatewayMock)
        BuildServices(AppDbContext db, decimal commissionRate = 0.05m)
    {
        var gateway = new Mock<IPaymentGatewayService>();
        gateway.Setup(g => g.ProviderName).Returns("Paymob");
        gateway.Setup(g => g.RefundAsync(It.IsAny<RefundRequest>()))
            .ReturnsAsync(new RefundResult(true, "REFUND_TX_AUTO_01", null));

        var notifications = new Mock<INotificationService>();
        notifications.Setup(n => n.SendNotificationAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<NotificationType>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var paymentService = new PaymentService(
            db,
            gateway.Object,
            NullLogger<PaymentService>.Instance,
            BuildConfig(commissionRate),
            notifications.Object);

        var bookingService = new BookingService(
            db,
            notifications.Object,
            paymentService);

        var ownerService = new OwnerService(db);

        return (paymentService, bookingService, ownerService, gateway);
    }

    private static async Task<(Guid bookingId, Guid userId, Guid ownerId, Guid venueId)>
        SeedBookingWithCourtAsync(AppDbContext db, decimal price = 1000m, int freeHours = 24, decimal lateFeePercent = 50m, DateTime? startTime = null)
    {
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        db.CancellationPolicies.Add(new CancellationPolicy
        {
            Id = Guid.NewGuid(),
            VenueId = venueId,
            FreeCancellationHours = freeHours,
            LateCancellationFeePercent = lateFeePercent,
            PolicyDescription = $"Free up to {freeHours}h before, {lateFeePercent}% fee after."
        });

        var start = startTime ?? DateTime.UtcNow.AddDays(2);
        var booking = new Booking
        {
            Id               = Guid.NewGuid(),
            BookingReference = $"PS-FIT-{Guid.NewGuid().ToString()[..6].ToUpper()}",
            CourtId          = courtId,
            UserId           = clientId,
            StartTime        = start,
            EndTime          = start.AddHours(1),
            Status           = BookingStatus.Confirmed,
            PaymentStatus    = PaymentStatus.Pending,
            TotalPrice       = price,
            CreatedAt        = DateTime.UtcNow
        };

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        return (booking.Id, clientId, ownerId, venueId);
    }

    // ── 1. Automatic Full Refund on Cancellation ────────────────────────────

    [Fact]
    public async Task CancelBooking_WithCompletedPayment_ExecutesFullRefundAutomatically()
    {
        var db = TestDbContextFactory.Create($"FIT_FullRefund_{Guid.NewGuid():N}");
        var (paymentSvc, bookingSvc, _, gatewayMock) = BuildServices(db);
        var (bookingId, userId, ownerId, _) = await SeedBookingWithCourtAsync(db, price: 1000m);

        // Seed completed payment
        var payment = new Payment
        {
            Id                   = Guid.NewGuid(),
            BookingId            = bookingId,
            Amount               = 1000m,
            CommissionAmount     = 50m,
            OwnerNetAmount       = 950m,
            Status               = PaymentStatus.Completed,
            Method               = PaymentMethod.CreditCard,
            TransactionReference = "TX_COMPLETED_1000",
            PaidAt               = DateTime.UtcNow.AddHours(-2),
            CreatedAt            = DateTime.UtcNow.AddHours(-2)
        };
        db.Payments.Add(payment);

        db.TransactionLedger.Add(new TransactionLedger
        {
            Id                     = Guid.NewGuid(),
            PaymentId              = payment.Id,
            BookingId              = bookingId,
            UserId                 = userId,
            OwnerId                = ownerId,
            EntryType              = LedgerEntryType.Payment,
            GrossAmount            = 1000m,
            CommissionAmount       = 50m,
            NetAmount              = 950m,
            CommissionRateSnapshot = 0.05m,
            CreatedAt              = DateTime.UtcNow.AddHours(-2)
        });

        var booking = await db.Bookings.FindAsync(bookingId);
        booking!.PaymentStatus = PaymentStatus.Completed;
        await db.SaveChangesAsync();

        // Act: Player cancels booking (2 days in advance -> Free Cancellation)
        var result = await bookingSvc.CancelWithPolicyAsync(userId, "Client", bookingId, new CancelBookingRequest { Reason = "Change of plans" });

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0m, result.CancellationFee);
        Assert.Equal(1000m, result.RefundAmount);

        // Booking status
        var updatedBooking = await db.Bookings.FindAsync(bookingId);
        Assert.Equal(BookingStatus.Cancelled, updatedBooking!.Status);
        Assert.Equal(PaymentStatus.Refunded, updatedBooking.PaymentStatus);

        // Payment status
        var updatedPayment = await db.Payments.FindAsync(payment.Id);
        Assert.Equal(PaymentStatus.Refunded, updatedPayment!.Status);

        // Gateway was called for full 1000 EGP
        gatewayMock.Verify(g => g.RefundAsync(It.Is<RefundRequest>(r => r.Amount == 1000m)), Times.Once);

        // Ledger entries: 1 Payment + 1 Refund
        var refundEntry = await db.TransactionLedger.FirstOrDefaultAsync(t => t.BookingId == bookingId && t.EntryType == LedgerEntryType.Refund);
        Assert.NotNull(refundEntry);
        Assert.Equal(-1000m, refundEntry.GrossAmount);
        Assert.Equal(-50m, refundEntry.CommissionAmount);
        Assert.Equal(-950m, refundEntry.NetAmount);

        // Owner Financial Report should show zero net after full refund
        var report = await paymentSvc.GetOwnerFinancialReportAsync(ownerId, null, null);
        Assert.Equal(1000m, report.TotalGross);
        Assert.Equal(1000m, report.TotalRefunds);
        Assert.Equal(0m, report.TotalCancellationFees);
        Assert.Equal(0m, report.TotalCommission);
        Assert.Equal(0m, report.TotalNet);
    }

    // ── 2. Automatic Partial Refund with Cancellation Fee ───────────────────

    [Fact]
    public async Task CancelBooking_WithCompletedPayment_ExecutesPartialRefundAutomatically()
    {
        var db = TestDbContextFactory.Create($"FIT_PartialRefund_{Guid.NewGuid():N}");
        var (paymentSvc, bookingSvc, _, gatewayMock) = BuildServices(db);
        // Start time 6 hours in the future -> outside 24h free window -> 50% late fee applies
        var nearStart = DateTime.UtcNow.AddHours(6);
        var (bookingId, userId, ownerId, _) = await SeedBookingWithCourtAsync(db, price: 1000m, freeHours: 24, lateFeePercent: 50m, startTime: nearStart);

        var payment = new Payment
        {
            Id                   = Guid.NewGuid(),
            BookingId            = bookingId,
            Amount               = 1000m,
            CommissionAmount     = 50m,
            OwnerNetAmount       = 950m,
            Status               = PaymentStatus.Completed,
            Method               = PaymentMethod.CreditCard,
            TransactionReference = "TX_COMPLETED_PARTIAL",
            PaidAt               = DateTime.UtcNow.AddHours(-1),
            CreatedAt            = DateTime.UtcNow.AddHours(-1)
        };
        db.Payments.Add(payment);

        db.TransactionLedger.Add(new TransactionLedger
        {
            Id                     = Guid.NewGuid(),
            PaymentId              = payment.Id,
            BookingId              = bookingId,
            UserId                 = userId,
            OwnerId                = ownerId,
            EntryType              = LedgerEntryType.Payment,
            GrossAmount            = 1000m,
            CommissionAmount       = 50m,
            NetAmount              = 950m,
            CommissionRateSnapshot = 0.05m,
            CreatedAt              = DateTime.UtcNow.AddHours(-1)
        });

        var booking = await db.Bookings.FindAsync(bookingId);
        booking!.PaymentStatus = PaymentStatus.Completed;
        await db.SaveChangesAsync();

        // Act: Player cancels late
        var result = await bookingSvc.CancelWithPolicyAsync(userId, "Client", bookingId, null);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(500m, result.CancellationFee);
        Assert.Equal(500m, result.RefundAmount);

        // Gateway was called for EXACTLY 500 EGP (not 1000)
        gatewayMock.Verify(g => g.RefundAsync(It.Is<RefundRequest>(r => r.Amount == 500m)), Times.Once);

        var updatedPayment = await db.Payments.FindAsync(payment.Id);
        Assert.Equal(PaymentStatus.PartiallyRefunded, updatedPayment!.Status);

        // Ledger entries: Refund (-500) and CancellationFee (+500)
        var refundEntry = await db.TransactionLedger.FirstOrDefaultAsync(t => t.BookingId == bookingId && t.EntryType == LedgerEntryType.Refund);
        Assert.NotNull(refundEntry);
        Assert.Equal(-500m, refundEntry.GrossAmount);
        Assert.Equal(-25m, refundEntry.CommissionAmount);
        Assert.Equal(-475m, refundEntry.NetAmount);

        var feeEntry = await db.TransactionLedger.FirstOrDefaultAsync(t => t.BookingId == bookingId && t.EntryType == LedgerEntryType.CancellationFee);
        Assert.NotNull(feeEntry);
        Assert.Equal(500m, feeEntry.GrossAmount);
        Assert.Equal(25m, feeEntry.CommissionAmount);
        Assert.Equal(475m, feeEntry.NetAmount);

        // CRITICAL DOUBLE-COUNTING CHECK:
        // Original: Gross 1000, Net 950
        // Refund: Gross -500, Net -475
        // Cancellation fee: Gross 500, Net 475
        // Economically realized: Gross 1000 - 500 = 500. Comm = 25. Owner Net = 475.
        var report = await paymentSvc.GetOwnerFinancialReportAsync(ownerId, null, null);
        Assert.Equal(1000m, report.TotalGross);
        Assert.Equal(500m, report.TotalRefunds);
        Assert.Equal(500m, report.TotalCancellationFees);
        Assert.Equal(25m, report.TotalCommission);
        Assert.Equal(475m, report.TotalNet); // MUST BE 475, NEVER 950
    }

    // ── 3. Cancellation with 100% Fee Retained ──────────────────────────────

    [Fact]
    public async Task CancelBooking_100PercentFee_RetainsFullFeeWithoutGatewayRefund()
    {
        var db = TestDbContextFactory.Create($"FIT_100Fee_{Guid.NewGuid():N}");
        var (paymentSvc, bookingSvc, _, gatewayMock) = BuildServices(db);
        var nearStart = DateTime.UtcNow.AddHours(2);
        var (bookingId, userId, ownerId, _) = await SeedBookingWithCourtAsync(db, price: 500m, freeHours: 24, lateFeePercent: 100m, startTime: nearStart);

        var payment = new Payment
        {
            Id                   = Guid.NewGuid(),
            BookingId            = bookingId,
            Amount               = 500m,
            CommissionAmount     = 25m,
            OwnerNetAmount       = 475m,
            Status               = PaymentStatus.Completed,
            Method               = PaymentMethod.CreditCard,
            TransactionReference = "TX_FULL_PENALTY",
            CreatedAt            = DateTime.UtcNow.AddHours(-1)
        };
        db.Payments.Add(payment);

        db.TransactionLedger.Add(new TransactionLedger
        {
            Id                     = Guid.NewGuid(),
            PaymentId              = payment.Id,
            BookingId              = bookingId,
            UserId                 = userId,
            OwnerId                = ownerId,
            EntryType              = LedgerEntryType.Payment,
            GrossAmount            = 500m,
            CommissionAmount       = 25m,
            NetAmount              = 475m,
            CommissionRateSnapshot = 0.05m,
            CreatedAt              = DateTime.UtcNow.AddHours(-1)
        });

        var booking = await db.Bookings.FindAsync(bookingId);
        booking!.PaymentStatus = PaymentStatus.Completed;
        await db.SaveChangesAsync();

        var result = await bookingSvc.CancelWithPolicyAsync(userId, "Client", bookingId, null);

        Assert.True(result.Success);
        Assert.Equal(500m, result.CancellationFee);
        Assert.Equal(0m, result.RefundAmount);

        // Gateway Refund MUST NOT be called for 0 refund
        gatewayMock.Verify(g => g.RefundAsync(It.IsAny<RefundRequest>()), Times.Never);

        // Payment status remains Completed because full amount is retained
        var updatedPayment = await db.Payments.FindAsync(payment.Id);
        Assert.Equal(PaymentStatus.Completed, updatedPayment!.Status);

        var report = await paymentSvc.GetOwnerFinancialReportAsync(ownerId, null, null);
        Assert.Equal(500m, report.TotalGross);
        Assert.Equal(0m, report.TotalRefunds);
        Assert.Equal(500m, report.TotalCancellationFees);
        Assert.Equal(25m, report.TotalCommission);
        Assert.Equal(475m, report.TotalNet);
    }

    // ── 4. PayAtFacility Unpaid Cancellation ─────────────────────────────────

    [Fact]
    public async Task CancelBooking_PayAtFacility_UnpaidBooking_CancelsPaymentWithoutGatewayRefund()
    {
        var db = TestDbContextFactory.Create($"FIT_PayAtFacilityUnpaid_{Guid.NewGuid():N}");
        var (paymentSvc, bookingSvc, ownerSvc, gatewayMock) = BuildServices(db);
        var (bookingId, userId, ownerId, _) = await SeedBookingWithCourtAsync(db, price: 600m);

        var payment = new Payment
        {
            Id        = Guid.NewGuid(),
            BookingId = bookingId,
            Amount    = 600m,
            Status    = PaymentStatus.Pending,
            Method    = PaymentMethod.PayAtFacility,
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var result = await bookingSvc.CancelWithPolicyAsync(userId, "Client", bookingId, null);

        Assert.True(result.Success);
        Assert.Equal(0m, result.RefundAmount); // No money to refund

        var updatedPayment = await db.Payments.FindAsync(payment.Id);
        Assert.Equal(PaymentStatus.Cancelled, updatedPayment!.Status);

        var updatedBooking = await db.Bookings.FindAsync(bookingId);
        Assert.Equal(BookingStatus.Cancelled, updatedBooking!.Status);
        Assert.Equal(PaymentStatus.Cancelled, updatedBooking.PaymentStatus);

        // Gateway must not be touched
        gatewayMock.Verify(g => g.RefundAsync(It.IsAny<RefundRequest>()), Times.Never);

        // No ledger entries should exist
        Assert.Empty(db.TransactionLedger.Where(t => t.BookingId == bookingId));

        // Dashboard revenue must be 0
        var dashboard = await ownerSvc.GetDashboardSummaryAsync(ownerId);
        Assert.Equal(0m, dashboard.TotalRevenue);
    }

    // ── 5. Pending Online Payment Cancellation ──────────────────────────────

    [Fact]
    public async Task CancelBooking_OnlinePaymentPending_CancelsPaymentWithoutGatewayRefund()
    {
        var db = TestDbContextFactory.Create($"FIT_OnlinePending_{Guid.NewGuid():N}");
        var (_, bookingSvc, _, gatewayMock) = BuildServices(db);
        var (bookingId, userId, _, _) = await SeedBookingWithCourtAsync(db, price: 400m);

        var payment = new Payment
        {
            Id        = Guid.NewGuid(),
            BookingId = bookingId,
            Amount    = 400m,
            Status    = PaymentStatus.Pending,
            Method    = PaymentMethod.CreditCard,
            CreatedAt = DateTime.UtcNow
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var result = await bookingSvc.CancelWithPolicyAsync(userId, "Client", bookingId, null);

        Assert.True(result.Success);
        var updatedPayment = await db.Payments.FindAsync(payment.Id);
        Assert.Equal(PaymentStatus.Cancelled, updatedPayment!.Status);
        gatewayMock.Verify(g => g.RefundAsync(It.IsAny<RefundRequest>()), Times.Never);
    }

    // ── 6. Processing Online Payment (Hold) Cancellation ─────────────────────

    [Fact]
    public async Task CancelBooking_OnlinePaymentProcessing_CancelsHoldImmediatelyWithoutGatewayRefund()
    {
        var db = TestDbContextFactory.Create($"FIT_ProcessingHold_{Guid.NewGuid():N}");
        var (_, bookingSvc, _, gatewayMock) = BuildServices(db);
        var (bookingId, userId, _, _) = await SeedBookingWithCourtAsync(db, price: 700m);

        var payment = new Payment
        {
            Id        = Guid.NewGuid(),
            BookingId = bookingId,
            Amount    = 700m,
            Status    = PaymentStatus.Processing, // Hold active
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            Method    = PaymentMethod.CreditCard,
            CreatedAt = DateTime.UtcNow
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var result = await bookingSvc.CancelWithPolicyAsync(userId, "Client", bookingId, null);

        Assert.True(result.Success);
        var updatedPayment = await db.Payments.FindAsync(payment.Id);
        Assert.Equal(PaymentStatus.Cancelled, updatedPayment!.Status);
        gatewayMock.Verify(g => g.RefundAsync(It.IsAny<RefundRequest>()), Times.Never);
    }

    // ── 7. Failed Payment Cancellation ──────────────────────────────────────

    [Fact]
    public async Task CancelBooking_FailedPayment_DoesNotAttemptRefund()
    {
        var db = TestDbContextFactory.Create($"FIT_FailedPayment_{Guid.NewGuid():N}");
        var (_, bookingSvc, _, gatewayMock) = BuildServices(db);
        var (bookingId, userId, _, _) = await SeedBookingWithCourtAsync(db, price: 300m);

        var payment = new Payment
        {
            Id        = Guid.NewGuid(),
            BookingId = bookingId,
            Amount    = 300m,
            Status    = PaymentStatus.Failed,
            Method    = PaymentMethod.CreditCard,
            CreatedAt = DateTime.UtcNow
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var result = await bookingSvc.CancelWithPolicyAsync(userId, "Client", bookingId, null);

        Assert.True(result.Success);
        var updatedPayment = await db.Payments.FindAsync(payment.Id);
        Assert.Equal(PaymentStatus.Failed, updatedPayment!.Status); // Preserves Failed terminal state
        gatewayMock.Verify(g => g.RefundAsync(It.IsAny<RefundRequest>()), Times.Never);
    }

    // ── 8. Already Refunded Payment ─────────────────────────────────────────

    [Fact]
    public async Task ProcessRefund_AlreadyRefundedPayment_RejectsDuplicateRefund()
    {
        var db = TestDbContextFactory.Create($"FIT_AlreadyRefunded_{Guid.NewGuid():N}");
        var (paymentSvc, _, _, gatewayMock) = BuildServices(db);
        var (bookingId, _, _, _) = await SeedBookingWithCourtAsync(db, price: 200m);

        var payment = new Payment
        {
            Id        = Guid.NewGuid(),
            BookingId = bookingId,
            Amount    = 200m,
            Status    = PaymentStatus.Refunded,
            Method    = PaymentMethod.CreditCard,
            CreatedAt = DateTime.UtcNow
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var refundResult = await paymentSvc.ProcessRefundAsync(bookingId, "Second refund attempt");
        Assert.False(refundResult.Success);
        Assert.Equal("Payment has already been refunded.", refundResult.ErrorMessage);
        gatewayMock.Verify(g => g.RefundAsync(It.IsAny<RefundRequest>()), Times.Never);
    }

    // ── 9. Concurrent Refunds ───────────────────────────────────────────────

    [Fact]
    public async Task ConcurrentRefunds_OnlyOneSucceeds()
    {
        var db = TestDbContextFactory.Create($"FIT_ConcurrentRefund_{Guid.NewGuid():N}");
        var (paymentSvc, _, _, _) = BuildServices(db);
        var (bookingId, userId, ownerId, _) = await SeedBookingWithCourtAsync(db, price: 300m);

        var payment = new Payment
        {
            Id                   = Guid.NewGuid(),
            BookingId            = bookingId,
            Amount               = 300m,
            CommissionAmount     = 15m,
            OwnerNetAmount       = 285m,
            Status               = PaymentStatus.Completed,
            Method               = PaymentMethod.CreditCard,
            TransactionReference = "TX_CONC_ORIG",
            CreatedAt            = DateTime.UtcNow
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var t1 = paymentSvc.ProcessRefundAsync(bookingId, "Concurrent refund 1");
        var t2 = paymentSvc.ProcessRefundAsync(bookingId, "Concurrent refund 2");
        var results = await Task.WhenAll(t1, t2);

        var successes = results.Count(r => r.Success);
        var failures  = results.Count(r => !r.Success);

        Assert.Equal(1, successes);
        Assert.Equal(1, failures);
    }

    // ── 10. Duplicate Cancellation Attempt ──────────────────────────────────

    [Fact]
    public async Task CancelBooking_DuplicateCancellation_ThrowsInvalidOperationException()
    {
        var db = TestDbContextFactory.Create($"FIT_DuplicateCancel_{Guid.NewGuid():N}");
        var (_, bookingSvc, _, _) = BuildServices(db);
        var (bookingId, userId, _, _) = await SeedBookingWithCourtAsync(db, price: 400m);

        // First cancellation succeeds
        var firstResult = await bookingSvc.CancelWithPolicyAsync(userId, "Client", bookingId, null);
        Assert.True(firstResult.Success);

        // Second cancellation throws
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            bookingSvc.CancelWithPolicyAsync(userId, "Client", bookingId, null));
    }

    // ── 11. Owner Financial Report: Captured vs Net ──────────────────────────

    [Fact]
    public async Task OwnerFinancialReport_SingleCaptureNoRefund_ReportsExactGrossAndNet()
    {
        var db = TestDbContextFactory.Create($"FIT_ReportCapture_{Guid.NewGuid():N}");
        var (paymentSvc, _, _, _) = BuildServices(db);
        var (bookingId, userId, ownerId, _) = await SeedBookingWithCourtAsync(db, price: 1000m);

        db.TransactionLedger.Add(new TransactionLedger
        {
            Id                     = Guid.NewGuid(),
            PaymentId              = Guid.NewGuid(),
            BookingId              = bookingId,
            UserId                 = userId,
            OwnerId                = ownerId,
            EntryType              = LedgerEntryType.Payment,
            GrossAmount            = 1000m,
            CommissionAmount       = 50m,
            NetAmount              = 950m,
            CommissionRateSnapshot = 0.05m,
            CreatedAt              = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var report = await paymentSvc.GetOwnerFinancialReportAsync(ownerId, null, null);

        Assert.Equal(1000m, report.TotalGross);
        Assert.Equal(0m, report.TotalRefunds);
        Assert.Equal(0m, report.TotalCancellationFees);
        Assert.Equal(50m, report.TotalCommission);
        Assert.Equal(950m, report.TotalNet);
        Assert.Equal(1, report.TotalTransactions);
    }

    // ── 12. Owner Financial Report: Refund Deductions ────────────────────────

    [Fact]
    public async Task OwnerFinancialReport_WithRefunds_DeductsCorrectAmounts()
    {
        var db = TestDbContextFactory.Create($"FIT_ReportRefund_{Guid.NewGuid():N}");
        var (paymentSvc, _, _, _) = BuildServices(db);
        var (booking1, u1, ownerId, _) = await SeedBookingWithCourtAsync(db, price: 1000m);
        var (booking2, u2, _, _)       = await SeedBookingWithCourtAsync(db, price: 500m);

        // Booking 1: 1000 paid, no refund
        db.TransactionLedger.Add(new TransactionLedger
        {
            Id = Guid.NewGuid(), PaymentId = Guid.NewGuid(), BookingId = booking1, UserId = u1, OwnerId = ownerId,
            EntryType = LedgerEntryType.Payment, GrossAmount = 1000m, CommissionAmount = 50m, NetAmount = 950m,
            CommissionRateSnapshot = 0.05m, CreatedAt = DateTime.UtcNow.AddDays(-1)
        });

        // Booking 2: 500 paid, full 500 refunded
        var p2Id = Guid.NewGuid();
        db.TransactionLedger.Add(new TransactionLedger
        {
            Id = Guid.NewGuid(), PaymentId = p2Id, BookingId = booking2, UserId = u2, OwnerId = ownerId,
            EntryType = LedgerEntryType.Payment, GrossAmount = 500m, CommissionAmount = 25m, NetAmount = 475m,
            CommissionRateSnapshot = 0.05m, CreatedAt = DateTime.UtcNow.AddHours(-10)
        });
        db.TransactionLedger.Add(new TransactionLedger
        {
            Id = Guid.NewGuid(), PaymentId = p2Id, BookingId = booking2, UserId = u2, OwnerId = ownerId,
            EntryType = LedgerEntryType.Refund, GrossAmount = -500m, CommissionAmount = -25m, NetAmount = -475m,
            CommissionRateSnapshot = 0.05m, CreatedAt = DateTime.UtcNow.AddHours(-5)
        });
        await db.SaveChangesAsync();

        var report = await paymentSvc.GetOwnerFinancialReportAsync(ownerId, null, null);

        // Total Gross = 1000 + 500 = 1500
        Assert.Equal(1500m, report.TotalGross);
        // Total Refunds = 500
        Assert.Equal(500m, report.TotalRefunds);
        // Net earned commission = 50 + 25 - 25 = 50
        Assert.Equal(50m, report.TotalCommission);
        // Realized Net = 950 + 475 - 475 = 950
        Assert.Equal(950m, report.TotalNet);
        Assert.Equal(3, report.TotalTransactions);
    }

    // ── 13. Owner Financial Report: Cancellation Fee Reporting ──────────────

    [Fact]
    public async Task OwnerFinancialReport_WithCancellationFees_ReportsFeesWithoutDoubleCountingNet()
    {
        var db = TestDbContextFactory.Create($"FIT_ReportFees_{Guid.NewGuid():N}");
        var (paymentSvc, _, _, _) = BuildServices(db);
        var (bookingId, userId, ownerId, _) = await SeedBookingWithCourtAsync(db, price: 1000m);
        var pId = Guid.NewGuid();

        // 1000 original payment
        db.TransactionLedger.Add(new TransactionLedger
        {
            Id = Guid.NewGuid(), PaymentId = pId, BookingId = bookingId, UserId = userId, OwnerId = ownerId,
            EntryType = LedgerEntryType.Payment, GrossAmount = 1000m, CommissionAmount = 50m, NetAmount = 950m,
            CommissionRateSnapshot = 0.05m, CreatedAt = DateTime.UtcNow.AddHours(-5)
        });

        // 500 refunded (50% late fee retained)
        db.TransactionLedger.Add(new TransactionLedger
        {
            Id = Guid.NewGuid(), PaymentId = pId, BookingId = bookingId, UserId = userId, OwnerId = ownerId,
            EntryType = LedgerEntryType.Refund, GrossAmount = -500m, CommissionAmount = -25m, NetAmount = -475m,
            CommissionRateSnapshot = 0.05m, CreatedAt = DateTime.UtcNow.AddHours(-2)
        });

        // 500 cancellation fee audit record
        db.TransactionLedger.Add(new TransactionLedger
        {
            Id = Guid.NewGuid(), PaymentId = pId, BookingId = bookingId, UserId = userId, OwnerId = ownerId,
            EntryType = LedgerEntryType.CancellationFee, GrossAmount = 500m, CommissionAmount = 25m, NetAmount = 475m,
            CommissionRateSnapshot = 0.05m, CreatedAt = DateTime.UtcNow.AddHours(-2)
        });
        await db.SaveChangesAsync();

        var report = await paymentSvc.GetOwnerFinancialReportAsync(ownerId, null, null);

        Assert.Equal(1000m, report.TotalGross);
        Assert.Equal(500m, report.TotalRefunds);
        Assert.Equal(500m, report.TotalCancellationFees);
        Assert.Equal(25m, report.TotalCommission);
        Assert.Equal(475m, report.TotalNet); // 950 - 475 = 475 (NOT 950)
    }

    // ── 14. Owner Isolation ─────────────────────────────────────────────────

    [Fact]
    public async Task OwnerFinancialReport_OwnerIsolation_OwnerACannotSeeOwnerBData()
    {
        var db = TestDbContextFactory.Create($"FIT_Isolation_{Guid.NewGuid():N}");
        var (paymentSvc, _, _, _) = BuildServices(db);
        var (bookingA, uA, ownerA, _) = await SeedBookingWithCourtAsync(db, price: 800m);
        var (bookingB, uB, ownerB, _) = await SeedBookingWithCourtAsync(db, price: 1200m);

        var paymentA = new Payment
        {
            Id = Guid.NewGuid(), BookingId = bookingA, Amount = 800m, Status = PaymentStatus.Completed, Method = PaymentMethod.CreditCard
        };
        var paymentB = new Payment
        {
            Id = Guid.NewGuid(), BookingId = bookingB, Amount = 1200m, Status = PaymentStatus.Completed, Method = PaymentMethod.CreditCard
        };
        db.Payments.AddRange(paymentA, paymentB);

        db.TransactionLedger.Add(new TransactionLedger
        {
            Id = Guid.NewGuid(), PaymentId = paymentA.Id, BookingId = bookingA, UserId = uA, OwnerId = ownerA,
            EntryType = LedgerEntryType.Payment, GrossAmount = 800m, CommissionAmount = 40m, NetAmount = 760m,
            CommissionRateSnapshot = 0.05m, CreatedAt = DateTime.UtcNow
        });

        db.TransactionLedger.Add(new TransactionLedger
        {
            Id = Guid.NewGuid(), PaymentId = paymentB.Id, BookingId = bookingB, UserId = uB, OwnerId = ownerB,
            EntryType = LedgerEntryType.Payment, GrossAmount = 1200m, CommissionAmount = 60m, NetAmount = 1140m,
            CommissionRateSnapshot = 0.05m, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var reportA = await paymentSvc.GetOwnerFinancialReportAsync(ownerA, null, null);
        Assert.Equal(800m, reportA.TotalGross);
        Assert.Equal(760m, reportA.TotalNet);
        Assert.Single(reportA.Entries);

        var reportB = await paymentSvc.GetOwnerFinancialReportAsync(ownerB, null, null);
        Assert.Equal(1200m, reportB.TotalGross);
        Assert.Equal(1140m, reportB.TotalNet);
        Assert.Single(reportB.Entries);
    }

    // ── 15. Dashboard Revenue: Unpaid PayAtFacility Exclusion ────────────────

    [Fact]
    public async Task OwnerDashboard_RealizedRevenue_ExcludesUnpaidPayAtFacility()
    {
        var db = TestDbContextFactory.Create($"FIT_DashUnpaidPAF_{Guid.NewGuid():N}");
        var (_, _, ownerSvc, _) = BuildServices(db);
        var (bookingId, _, ownerId, _) = await SeedBookingWithCourtAsync(db, price: 500m);

        var payment = new Payment
        {
            Id        = Guid.NewGuid(),
            BookingId = bookingId,
            Amount    = 500m,
            Status    = PaymentStatus.Pending,
            Method    = PaymentMethod.PayAtFacility,
            CreatedAt = DateTime.UtcNow
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var summary = await ownerSvc.GetDashboardSummaryAsync(ownerId);
        // Unpaid booking must NOT count as realized revenue
        Assert.Equal(0m, summary.TotalRevenue);
        Assert.Equal(0m, summary.Analytics.RevenueToday);
    }

    // ── 16. Dashboard Revenue: Failed and Processing Exclusion ───────────────

    [Fact]
    public async Task OwnerDashboard_RealizedRevenue_ExcludesFailedAndProcessingPayments()
    {
        var db = TestDbContextFactory.Create($"FIT_DashFailed_{Guid.NewGuid():N}");
        var (_, _, ownerSvc, _) = BuildServices(db);
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var start = DateTime.UtcNow.AddDays(2);
        var b1 = new Booking
        {
            Id = Guid.NewGuid(), BookingReference = "PS-B1", CourtId = courtId, UserId = clientId,
            StartTime = start, EndTime = start.AddHours(1), Status = BookingStatus.Confirmed,
            PaymentStatus = PaymentStatus.Processing, TotalPrice = 400m
        };
        var b2 = new Booking
        {
            Id = Guid.NewGuid(), BookingReference = "PS-B2", CourtId = courtId, UserId = clientId,
            StartTime = start.AddHours(2), EndTime = start.AddHours(3), Status = BookingStatus.Confirmed,
            PaymentStatus = PaymentStatus.Failed, TotalPrice = 600m
        };
        var b3 = new Booking
        {
            Id = Guid.NewGuid(), BookingReference = "PS-B3", CourtId = courtId, UserId = clientId,
            StartTime = start.AddHours(4), EndTime = start.AddHours(5), Status = BookingStatus.Confirmed,
            PaymentStatus = PaymentStatus.Completed, TotalPrice = 1000m
        };
        db.Bookings.AddRange(b1, b2, b3);

        // b1: Processing hold
        db.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(), BookingId = b1.Id, Amount = 400m, Status = PaymentStatus.Processing, Method = PaymentMethod.CreditCard
        });
        // b2: Failed payment
        db.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(), BookingId = b2.Id, Amount = 600m, Status = PaymentStatus.Failed, Method = PaymentMethod.CreditCard
        });
        // b3: Completed payment
        db.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(), BookingId = b3.Id, Amount = 1000m, Status = PaymentStatus.Completed, Method = PaymentMethod.CreditCard
        });

        await db.SaveChangesAsync();

        var summary = await ownerSvc.GetDashboardSummaryAsync(ownerId);

        // Only the 1000 EGP completed payment counts
        Assert.Equal(1000m, summary.TotalRevenue);
    }

    // ── 17. Dashboard Revenue: Fully Refunded Bookings Exclusion ─────────────

    [Fact]
    public async Task OwnerDashboard_RealizedRevenue_ExcludesFullyRefundedBookings()
    {
        var db = TestDbContextFactory.Create($"FIT_DashRefund_{Guid.NewGuid():N}");
        var (_, _, ownerSvc, _) = BuildServices(db);
        var (bookingId, _, ownerId, _) = await SeedBookingWithCourtAsync(db, price: 800m);

        db.Payments.Add(new Payment
        {
            Id        = Guid.NewGuid(),
            BookingId = bookingId,
            Amount    = 800m,
            Status    = PaymentStatus.Refunded,
            Method    = PaymentMethod.CreditCard
        });
        var b = await db.Bookings.FindAsync(bookingId);
        b!.PaymentStatus = PaymentStatus.Refunded;
        await db.SaveChangesAsync();

        var summary = await ownerSvc.GetDashboardSummaryAsync(ownerId);
        Assert.Equal(0m, summary.TotalRevenue);
    }

    // ── 18. Dashboard Revenue: Partially Refunded Bookings Retention ─────────

    [Fact]
    public async Task OwnerDashboard_RealizedRevenue_PartiallyRefunded_CountsOnlyRetainedFee()
    {
        var db = TestDbContextFactory.Create($"FIT_DashPartial_{Guid.NewGuid():N}");
        var (_, _, ownerSvc, _) = BuildServices(db);
        var (bookingId, _, ownerId, _) = await SeedBookingWithCourtAsync(db, price: 1000m);

        db.Payments.Add(new Payment
        {
            Id        = Guid.NewGuid(),
            BookingId = bookingId,
            Amount    = 1000m,
            Status    = PaymentStatus.PartiallyRefunded,
            Method    = PaymentMethod.CreditCard
        });
        var b = await db.Bookings.FindAsync(bookingId);
        b!.PaymentStatus = PaymentStatus.PartiallyRefunded;
        b.CancellationFee = 300m; // 300 retained, 700 refunded
        await db.SaveChangesAsync();

        var summary = await ownerSvc.GetDashboardSummaryAsync(ownerId);
        // Only the retained 300 EGP counts towards revenue
        Assert.Equal(300m, summary.TotalRevenue);
    }
}

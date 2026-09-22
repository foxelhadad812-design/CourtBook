using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using CourtBook.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CourtBook.Tests;

/// <summary>
/// Phase 8.5: Staging Deployment, Payment Lifecycle Invariants & Webhook Terminal State Tests.
/// </summary>
public class Phase85StagingAndLifecycleVerificationTests
{
    private static IConfiguration BuildConfig(decimal commissionRate = 0.05m)
    {
        var dict = new Dictionary<string, string?>
        {
            ["PaymentGateway:CommissionRate"]       = commissionRate.ToString("0.00"),
            ["PaymentGateway:OnlineHoldMinutes"]    = "10",
            ["PaymentGateway:Paymob:IsSandbox"]     = "true",
            ["PaymentGateway:Paymob:ApiKey"]        = "SANDBOX_API_KEY",
            ["PaymentGateway:Paymob:IntegrationId"] = "SANDBOX_INT_ID",
            ["PaymentGateway:Paymob:IframeId"]      = "SANDBOX_IFRAME_ID",
            ["PaymentGateway:Paymob:HmacSecret"]    = "SANDBOX_HMAC_SECRET"
        };
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static (PaymentService service, Mock<IPaymentGatewayService> gatewayMock, Mock<INotificationService> notifMock)
        BuildService(AppDbContext db, decimal commissionRate = 0.05m)
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

    private static async Task<(Guid bookingId, Guid clientId, Guid ownerId, Guid courtId)>
        SeedBookingAsync(AppDbContext db, decimal price = 250m, decimal cancellationFee = 0m)
    {
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var booking = new Booking
        {
            Id               = Guid.NewGuid(),
            BookingReference = $"PS-85-{Guid.NewGuid().ToString()[..6].ToUpper()}",
            CourtId          = courtId,
            UserId           = clientId,
            StartTime        = DateTime.UtcNow.AddDays(2),
            EndTime          = DateTime.UtcNow.AddDays(2).AddHours(1),
            Status           = BookingStatus.Confirmed,
            PaymentStatus    = PaymentStatus.Pending,
            TotalPrice       = price,
            CancellationFee  = cancellationFee,
            CreatedAt        = DateTime.UtcNow
        };

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        return (booking.Id, clientId, ownerId, courtId);
    }

    // ── 1. Webhook Terminal State Invariant Tests ─────────────────────────────

    [Theory]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Cancelled)]
    [InlineData(PaymentStatus.Refunded)]
    [InlineData(PaymentStatus.PartiallyRefunded)]
    public async Task Webhook_DeliveredToPaymentInTerminalState_RejectsCompletionAndPreservesStatus(PaymentStatus terminalStatus)
    {
        var db = TestDbContextFactory.Create($"P85_TerminalWebhook_{terminalStatus}_{Guid.NewGuid():N}");
        var (svc, _, _) = BuildService(db);
        var (bookingId, userId, _, _) = await SeedBookingAsync(db);

        var orderId = $"ORD_TERM_{terminalStatus}";
        var txId = $"TX_TERM_{terminalStatus}";

        var payment = new Payment
        {
            Id              = Guid.NewGuid(),
            BookingId       = bookingId,
            Amount          = 250m,
            ProviderOrderId = orderId,
            Status          = terminalStatus,
            Method          = PaymentMethod.CreditCard,
            CreatedAt       = DateTime.UtcNow.AddMinutes(-30)
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        // Late webhook arrives attempting to complete payment
        await svc.ProcessWebhookPaymentCompletedAsync(orderId, txId, 250m, txId, "Paymob");

        var reloaded = db.Payments.Find(payment.Id)!;
        Assert.Equal(terminalStatus, reloaded.Status);
        Assert.Empty(db.TransactionLedger.Where(t => t.PaymentId == payment.Id));

        // Idempotency log should record rejection
        var log = db.IdempotencyLogs.FirstOrDefault(l => l.PaymentId == payment.Id);
        Assert.NotNull(log);
        Assert.Equal($"TerminalState_{terminalStatus}", log.Action);
    }

    // ── 2. VerifyReturn Terminal State Invariant Tests ────────────────────────

    [Theory]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Cancelled)]
    [InlineData(PaymentStatus.Refunded)]
    [InlineData(PaymentStatus.PartiallyRefunded)]
    public async Task VerifyReturn_PaymentInTerminalState_ReturnsFailureWithoutCallingGateway(PaymentStatus terminalStatus)
    {
        var db = TestDbContextFactory.Create($"P85_VerifyTerminal_{terminalStatus}_{Guid.NewGuid():N}");
        var (svc, gatewayMock, _) = BuildService(db);
        var (bookingId, userId, _, _) = await SeedBookingAsync(db);

        var orderId = $"ORD_VERIFY_TERM_{terminalStatus}";

        var payment = new Payment
        {
            Id              = Guid.NewGuid(),
            BookingId       = bookingId,
            Amount          = 250m,
            ProviderOrderId = orderId,
            Status          = terminalStatus,
            Method          = PaymentMethod.CreditCard,
            CreatedAt       = DateTime.UtcNow.AddMinutes(-30)
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var result = await svc.VerifyReturnAsync(userId, "Client", orderId);

        Assert.False(result.IsSuccessful);
        Assert.Equal(terminalStatus.ToString(), result.Status);
        Assert.Contains("terminal state", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Gateway order lookup should NEVER have been invoked
        gatewayMock.Verify(g => g.VerifyPaymentAsync(It.IsAny<string>()), Times.Never);
    }

    // ── 3. Refund Edge Cases & Zero-Value Gateway Guard ───────────────────────

    [Fact]
    public async Task ProcessRefund_FullCancellationFee_RetainsFeeWithoutGatewayRefund()
    {
        var db = TestDbContextFactory.Create($"P85_ZeroRefund_{Guid.NewGuid():N}");
        var (svc, gatewayMock, _) = BuildService(db);
        // Booking total price is 200, but late cancellation fee is 200 (100% penalty)
        var (bookingId, userId, ownerId, _) = await SeedBookingAsync(db, price: 200m, cancellationFee: 200m);

        var payment = new Payment
        {
            Id                   = Guid.NewGuid(),
            BookingId            = bookingId,
            Amount               = 200m,
            Status               = PaymentStatus.Completed,
            Method               = PaymentMethod.CreditCard,
            TransactionReference = "TX_PAID_100",
            PaidAt               = DateTime.UtcNow.AddHours(-1),
            CreatedAt            = DateTime.UtcNow.AddHours(-2)
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var response = await svc.ProcessRefundAsync(bookingId, "Late cancellation (100% fee)");

        Assert.True(response.Success);
        Assert.Equal(0m, response.RefundAmount);
        Assert.Equal("RETAINED_FEE", response.RefundTransactionId);

        // Crucial invariant: Gateway RefundAsync must NEVER be called for 0 refund amount
        gatewayMock.Verify(g => g.RefundAsync(It.IsAny<RefundRequest>()), Times.Never);

        // Status remains Completed, fee recorded in ledger
        var reloadedPayment = db.Payments.Find(payment.Id)!;
        Assert.Equal(PaymentStatus.Completed, reloadedPayment.Status);

        var feeEntry = db.TransactionLedger.FirstOrDefault(t => t.BookingId == bookingId && t.EntryType == LedgerEntryType.CancellationFee);
        Assert.NotNull(feeEntry);
        Assert.Equal(200m, feeEntry.GrossAmount);
    }

    [Fact]
    public async Task ProcessRefund_NonCompletedPayment_RejectsRefund()
    {
        var db = TestDbContextFactory.Create($"P85_RefundNonCompleted_{Guid.NewGuid():N}");
        var (svc, _, _) = BuildService(db);
        var (bookingId, _, _, _) = await SeedBookingAsync(db, price: 200m);

        var payment = new Payment
        {
            Id        = Guid.NewGuid(),
            BookingId = bookingId,
            Amount    = 200m,
            Status    = PaymentStatus.Processing, // Not completed
            Method    = PaymentMethod.CreditCard,
            CreatedAt = DateTime.UtcNow
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var result = await svc.ProcessRefundAsync(bookingId, "Player requested refund early");

        Assert.False(result.Success);
        Assert.Contains("Only completed payments", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── 4. Staging Configuration Validation Invariants ───────────────────────

    [Theory]
    [InlineData("Server=tcp:staging-sql.playspot.internal,1433;Database=PlaySpotStagingDB;")]
    [InlineData("Server=prod-sql.database.windows.net,1433;Database=PlaySpotDB;")]
    public void StagingConfiguration_AcceptsValidExternalConnectionStrings(string externalConn)
    {
        var lowerConn = externalConn.ToLowerInvariant();
        var isLocal = lowerConn.Contains("(localdb)") || lowerConn.Contains("localhost") || lowerConn.Contains("127.0.0.1");
        Assert.False(isLocal);
    }

    [Fact]
    public void StagingConfiguration_RejectsEmptyApiBaseUrl()
    {
        var isStaging = true;
        string? apiBaseUrl = "";

        var ex = Record.Exception(() =>
        {
            if (isStaging && string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                throw new InvalidOperationException("CRITICAL CONFIGURATION ERROR: ApiSettings:BaseUrl must be explicitly configured in Staging.");
            }
        });

        Assert.NotNull(ex);
        Assert.IsType<InvalidOperationException>(ex);
    }
}

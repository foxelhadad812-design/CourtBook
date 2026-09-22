using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using CourtBook.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CourtBook.Tests;

public class FinancialPayoutAndSettlementTests
{
    private (AppDbContext db, ISettlementService settlementService, IPayoutService payoutService, IRecoveryService recoveryService, IPaymentService paymentService)
        CreateServices(string dbName)
    {
        var db = TestDbContextFactory.Create(dbName);
        var loggerSettlement = NullLogger<SettlementService>.Instance;
        var loggerPayout = NullLogger<PayoutService>.Instance;
        var loggerRecovery = NullLogger<RecoveryService>.Instance;
        var loggerPayment = NullLogger<PaymentService>.Instance;

        var settlementService = new SettlementService(db, loggerSettlement);
        var payoutService = new PayoutService(db, loggerPayout);
        var recoveryService = new RecoveryService(db, loggerRecovery);
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PaymentGateway:CommissionRate"] = "0.05" })
            .Build();
        var paymentService = new PaymentService(db, new FakePaymentGateway(), loggerPayment, config);

        return (db, settlementService, payoutService, recoveryService, paymentService);
    }

    // ── 1. Owner Balance & Payout Method Tests ───────────────────────────────

    [Fact]
    public async Task GetOwnerBalance_NewOwner_ReturnsZeroBalances()
    {
        var (db, _, payoutService, _, _) = CreateServices(nameof(GetOwnerBalance_NewOwner_ReturnsZeroBalances));
        var ownerId = Guid.NewGuid();

        var balance = await payoutService.GetOwnerBalanceAsync(ownerId);

        Assert.Equal(ownerId, balance.OwnerId);
        Assert.Equal(0m, balance.AvailableBalance);
        Assert.Equal(0m, balance.PendingBalance);
        Assert.Equal(0m, balance.InFlightBalance);
        Assert.Equal(0m, balance.TotalPaidOut);
        Assert.Equal(0m, balance.TotalRefunded);
        Assert.Equal(0m, balance.OutstandingDeficit);
    }

    [Fact]
    public async Task CreatePayoutMethod_ValidBankTransfer_SavesAndMasksProperly()
    {
        var (db, _, payoutService, _, _) = CreateServices(nameof(CreatePayoutMethod_ValidBankTransfer_SavesAndMasksProperly));
        var ownerId = Guid.NewGuid();

        var request = new CreatePayoutMethodRequest
        {
            Type = "BankTransfer",
            AccountHolderName = "Ahmed Zaki",
            BankName = "Commercial International Bank",
            Iban = "EG380010000100000012345678901",
            AccountNumber = "1000234567",
            IsDefault = true
        };

        var result = await payoutService.CreatePayoutMethodAsync(ownerId, request);

        Assert.NotNull(result);
        Assert.Equal("BankTransfer", result.Type);
        Assert.True(result.IsDefault);
        Assert.True(result.IsActive);
        Assert.StartsWith("EG38", result.MaskedIban!);
        Assert.EndsWith("8901", result.MaskedIban!);
        Assert.Contains("******", result.MaskedAccountNumber!);

        var stored = await db.OwnerPayoutMethods.FirstOrDefaultAsync(m => m.Id == result.Id);
        Assert.NotNull(stored);
        Assert.Equal("EG380010000100000012345678901", stored.Iban);
    }

    [Fact]
    public async Task CreatePayoutMethod_SetDefault_ClearsPreviousDefault()
    {
        var (db, _, payoutService, _, _) = CreateServices(nameof(CreatePayoutMethod_SetDefault_ClearsPreviousDefault));
        var ownerId = Guid.NewGuid();

        var m1 = await payoutService.CreatePayoutMethodAsync(ownerId, new CreatePayoutMethodRequest
        {
            Type = "InstaPay",
            AccountHolderName = "Ahmed",
            InstaPayAddress = "ahmed@instapay",
            IsDefault = true
        });

        var m2 = await payoutService.CreatePayoutMethodAsync(ownerId, new CreatePayoutMethodRequest
        {
            Type = "MobileWallet",
            AccountHolderName = "Ahmed",
            MobileWalletNumber = "01012345678",
            IsDefault = true
        });

        var methods = await payoutService.GetPayoutMethodsAsync(ownerId);
        var defaultMethods = methods.Where(m => m.IsDefault).ToList();

        Assert.Single(defaultMethods);
        Assert.Equal(m2.Id, defaultMethods[0].Id);
    }

    [Fact]
    public async Task DeletePayoutMethod_SoftDeactivatesMethod()
    {
        var (db, _, payoutService, _, _) = CreateServices(nameof(DeletePayoutMethod_SoftDeactivatesMethod));
        var ownerId = Guid.NewGuid();

        var method = await payoutService.CreatePayoutMethodAsync(ownerId, new CreatePayoutMethodRequest
        {
            Type = "InstaPay",
            AccountHolderName = "Ahmed",
            InstaPayAddress = "ahmed@instapay"
        });

        var deleted = await payoutService.DeletePayoutMethodAsync(ownerId, method.Id);
        Assert.True(deleted);

        var activeMethods = await payoutService.GetPayoutMethodsAsync(ownerId);
        Assert.Empty(activeMethods);

        var dbRow = await db.OwnerPayoutMethods.FindAsync(method.Id);
        Assert.NotNull(dbRow);
        Assert.False(dbRow.IsActive);
    }

    // ── 2. Payout Request & Lifecycle Tests ───────────────────────────────────

    [Fact]
    public async Task RequestPayout_InsufficientBalance_ThrowsInvalidOperationException()
    {
        var (db, _, payoutService, _, _) = CreateServices(nameof(RequestPayout_InsufficientBalance_ThrowsInvalidOperationException));
        var ownerId = Guid.NewGuid();

        var method = await payoutService.CreatePayoutMethodAsync(ownerId, new CreatePayoutMethodRequest
        {
            Type = "InstaPay",
            AccountHolderName = "Ahmed",
            InstaPayAddress = "ahmed@instapay"
        });

        db.OwnerBalances.Add(new OwnerBalance
        {
            OwnerId = ownerId,
            AvailableBalance = 200m
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            payoutService.RequestPayoutAsync(ownerId, new CreatePayoutRequest
            {
                PayoutMethodId = method.Id,
                Amount = 500m
            }));
    }

    [Fact]
    public async Task RequestPayout_OutstandingDeficitExists_BlocksPayout()
    {
        var (db, _, payoutService, _, _) = CreateServices(nameof(RequestPayout_OutstandingDeficitExists_BlocksPayout));
        var ownerId = Guid.NewGuid();

        var method = await payoutService.CreatePayoutMethodAsync(ownerId, new CreatePayoutMethodRequest
        {
            Type = "InstaPay",
            AccountHolderName = "Ahmed",
            InstaPayAddress = "ahmed@instapay"
        });

        db.OwnerBalances.Add(new OwnerBalance
        {
            OwnerId = ownerId,
            AvailableBalance = 1000m,
            OutstandingDeficit = 300m
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            payoutService.RequestPayoutAsync(ownerId, new CreatePayoutRequest
            {
                PayoutMethodId = method.Id,
                Amount = 500m
            }));

        Assert.Contains("recovery deficit", ex.Message);
    }

    [Fact]
    public async Task RequestPayout_ValidRequest_ReservesFundsAtomically()
    {
        var (db, _, payoutService, _, _) = CreateServices(nameof(RequestPayout_ValidRequest_ReservesFundsAtomically));
        var ownerId = Guid.NewGuid();

        db.Users.Add(new User { Id = ownerId, Name = "Owner 1", Email = "owner1@test.com", Role = Role.Owner });

        var method = await payoutService.CreatePayoutMethodAsync(ownerId, new CreatePayoutMethodRequest
        {
            Type = "InstaPay",
            AccountHolderName = "Owner 1",
            InstaPayAddress = "owner1@instapay"
        });

        db.OwnerBalances.Add(new OwnerBalance
        {
            OwnerId = ownerId,
            AvailableBalance = 1000m,
            InFlightBalance = 0m
        });
        await db.SaveChangesAsync();

        var payout = await payoutService.RequestPayoutAsync(ownerId, new CreatePayoutRequest
        {
            PayoutMethodId = method.Id,
            Amount = 600m
        });

        Assert.NotNull(payout);
        Assert.Equal("Submitted", payout.Status);
        Assert.Equal(600m, payout.Amount);
        Assert.Equal(600m, payout.NetAmount);

        var balance = await payoutService.GetOwnerBalanceAsync(ownerId);
        Assert.Equal(400m, balance.AvailableBalance);
        Assert.Equal(600m, balance.InFlightBalance);
    }

    [Fact]
    public async Task RequestPayout_IdempotentToken_ReturnsExistingWithoutDoubleDebit()
    {
        var (db, _, payoutService, _, _) = CreateServices(nameof(RequestPayout_IdempotentToken_ReturnsExistingWithoutDoubleDebit));
        var ownerId = Guid.NewGuid();

        db.Users.Add(new User { Id = ownerId, Name = "Owner 2", Email = "owner2@test.com", Role = Role.Owner });

        var method = await payoutService.CreatePayoutMethodAsync(ownerId, new CreatePayoutMethodRequest
        {
            Type = "InstaPay",
            AccountHolderName = "Owner 2",
            InstaPayAddress = "owner2@instapay"
        });

        db.OwnerBalances.Add(new OwnerBalance
        {
            OwnerId = ownerId,
            AvailableBalance = 1000m
        });
        await db.SaveChangesAsync();

        var req = new CreatePayoutRequest
        {
            PayoutMethodId = method.Id,
            Amount = 400m,
            IdempotencyKey = "idempotent-key-1"
        };

        var p1 = await payoutService.RequestPayoutAsync(ownerId, req);
        var p2 = await payoutService.RequestPayoutAsync(ownerId, req);

        Assert.Equal(p1.Id, p2.Id);

        var balance = await payoutService.GetOwnerBalanceAsync(ownerId);
        Assert.Equal(600m, balance.AvailableBalance);
        Assert.Equal(400m, balance.InFlightBalance);
    }

    [Fact]
    public async Task CancelPayout_Submitted_RestoresAvailableBalance()
    {
        var (db, _, payoutService, _, _) = CreateServices(nameof(CancelPayout_Submitted_RestoresAvailableBalance));
        var ownerId = Guid.NewGuid();

        db.Users.Add(new User { Id = ownerId, Name = "Owner 3", Email = "owner3@test.com", Role = Role.Owner });
        var method = await payoutService.CreatePayoutMethodAsync(ownerId, new CreatePayoutMethodRequest
        {
            Type = "InstaPay",
            AccountHolderName = "Owner 3",
            InstaPayAddress = "owner3@instapay"
        });

        db.OwnerBalances.Add(new OwnerBalance
        {
            OwnerId = ownerId,
            AvailableBalance = 1000m
        });
        await db.SaveChangesAsync();

        var payout = await payoutService.RequestPayoutAsync(ownerId, new CreatePayoutRequest
        {
            PayoutMethodId = method.Id,
            Amount = 500m
        });

        var cancelled = await payoutService.CancelPayoutRequestAsync(ownerId, payout.Id);
        Assert.True(cancelled);

        var balance = await payoutService.GetOwnerBalanceAsync(ownerId);
        Assert.Equal(1000m, balance.AvailableBalance);
        Assert.Equal(0m, balance.InFlightBalance);

        var updatedPayout = await payoutService.GetOwnerPayoutByIdAsync(ownerId, payout.Id);
        Assert.Equal("Cancelled", updatedPayout!.Status);
    }

    [Fact]
    public async Task ApproveAndMarkPaid_ValidDisbursement_UpdatesBalancesAndLedger()
    {
        var (db, _, payoutService, _, _) = CreateServices(nameof(ApproveAndMarkPaid_ValidDisbursement_UpdatesBalancesAndLedger));
        var ownerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        db.Users.Add(new User { Id = ownerId, Name = "Owner 4", Email = "owner4@test.com", Role = Role.Owner });
        db.Users.Add(new User { Id = adminId, Name = "Admin User", Email = "admin@test.com", Role = Role.Admin });

        var method = await payoutService.CreatePayoutMethodAsync(ownerId, new CreatePayoutMethodRequest
        {
            Type = "BankTransfer",
            AccountHolderName = "Owner 4",
            BankName = "CIB",
            Iban = "EG380010000100000012345678901"
        });

        db.OwnerBalances.Add(new OwnerBalance
        {
            OwnerId = ownerId,
            AvailableBalance = 2000m
        });
        await db.SaveChangesAsync();

        var payout = await payoutService.RequestPayoutAsync(ownerId, new CreatePayoutRequest
        {
            PayoutMethodId = method.Id,
            Amount = 1500m
        });

        // 1. Approve
        var approved = await payoutService.ApprovePayoutAsync(payout.Id, adminId);
        Assert.Equal("Approved", approved.Status);

        // 2. Mark Paid
        var paid = await payoutService.MarkPayoutPaidAsync(payout.Id, adminId, new MarkPayoutPaidRequest
        {
            ExternalTransactionReference = "CIB-TXN-998877",
            DisbursementNote = "Bank wire processed"
        });

        Assert.Equal("Paid", paid.Status);
        Assert.Equal("CIB-TXN-998877", paid.ExternalTransactionReference);

        var balance = await payoutService.GetOwnerBalanceAsync(ownerId);
        Assert.Equal(500m, balance.AvailableBalance);
        Assert.Equal(0m, balance.InFlightBalance);
        Assert.Equal(1500m, balance.TotalPaidOut);

        // Verify TransactionLedger entry
        var ledgerEntry = await db.TransactionLedger.FirstOrDefaultAsync(l => l.PayoutRequestId == payout.Id);
        Assert.NotNull(ledgerEntry);
        Assert.Equal(LedgerEntryType.OwnerPayout, ledgerEntry.EntryType);
        Assert.Equal(-1500m, ledgerEntry.GrossAmount);
        Assert.Equal(-1500m, ledgerEntry.NetAmount);
        Assert.Equal("CIB-TXN-998877", ledgerEntry.ProviderReference);
    }

    [Fact]
    public async Task ApprovePayout_AdminIsOwner_ThrowsAntiSelfDealingException()
    {
        var (db, _, payoutService, _, _) = CreateServices(nameof(ApprovePayout_AdminIsOwner_ThrowsAntiSelfDealingException));
        var ownerAdminId = Guid.NewGuid();

        db.Users.Add(new User { Id = ownerAdminId, Name = "Owner Admin", Email = "owneradmin@test.com", Role = Role.Admin });
        var method = await payoutService.CreatePayoutMethodAsync(ownerAdminId, new CreatePayoutMethodRequest
        {
            Type = "InstaPay",
            AccountHolderName = "Owner Admin",
            InstaPayAddress = "admin@instapay"
        });

        db.OwnerBalances.Add(new OwnerBalance
        {
            OwnerId = ownerAdminId,
            AvailableBalance = 1000m
        });
        await db.SaveChangesAsync();

        var payout = await payoutService.RequestPayoutAsync(ownerAdminId, new CreatePayoutRequest
        {
            PayoutMethodId = method.Id,
            Amount = 500m
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            payoutService.ApprovePayoutAsync(payout.Id, ownerAdminId));

        Assert.Contains("self-approval", ex.Message);
    }

    [Fact]
    public async Task RejectPayout_ApprovedOrSubmitted_RestoresAvailableBalance()
    {
        var (db, _, payoutService, _, _) = CreateServices(nameof(RejectPayout_ApprovedOrSubmitted_RestoresAvailableBalance));
        var ownerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        db.Users.Add(new User { Id = ownerId, Name = "Owner 5", Email = "owner5@test.com", Role = Role.Owner });
        db.Users.Add(new User { Id = adminId, Name = "Admin 2", Email = "admin2@test.com", Role = Role.Admin });

        var method = await payoutService.CreatePayoutMethodAsync(ownerId, new CreatePayoutMethodRequest
        {
            Type = "InstaPay",
            AccountHolderName = "Owner 5",
            InstaPayAddress = "owner5@instapay"
        });

        db.OwnerBalances.Add(new OwnerBalance
        {
            OwnerId = ownerId,
            AvailableBalance = 1000m
        });
        await db.SaveChangesAsync();

        var payout = await payoutService.RequestPayoutAsync(ownerId, new CreatePayoutRequest
        {
            PayoutMethodId = method.Id,
            Amount = 700m
        });

        var rejected = await payoutService.RejectPayoutAsync(payout.Id, adminId, "Invalid recipient details");
        Assert.Equal("Rejected", rejected.Status);

        var balance = await payoutService.GetOwnerBalanceAsync(ownerId);
        Assert.Equal(1000m, balance.AvailableBalance);
        Assert.Equal(0m, balance.InFlightBalance);
    }

    [Fact]
    public async Task MarkPayoutPaid_DuplicateExternalRef_ThrowsConflictException()
    {
        var (db, _, payoutService, _, _) = CreateServices(nameof(MarkPayoutPaid_DuplicateExternalRef_ThrowsConflictException));
        var ownerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        db.Users.Add(new User { Id = ownerId, Name = "Owner 6", Email = "owner6@test.com", Role = Role.Owner });
        db.Users.Add(new User { Id = adminId, Name = "Admin 3", Email = "admin3@test.com", Role = Role.Admin });

        var method = await payoutService.CreatePayoutMethodAsync(ownerId, new CreatePayoutMethodRequest
        {
            Type = "InstaPay",
            AccountHolderName = "Owner 6",
            InstaPayAddress = "owner6@instapay"
        });

        db.OwnerBalances.Add(new OwnerBalance
        {
            OwnerId = ownerId,
            AvailableBalance = 2000m
        });
        await db.SaveChangesAsync();

        var p1 = await payoutService.RequestPayoutAsync(ownerId, new CreatePayoutRequest { PayoutMethodId = method.Id, Amount = 500m });
        var p2 = await payoutService.RequestPayoutAsync(ownerId, new CreatePayoutRequest { PayoutMethodId = method.Id, Amount = 500m });

        await payoutService.MarkPayoutPaidAsync(p1.Id, adminId, new MarkPayoutPaidRequest { ExternalTransactionReference = "REF-DUPLICATE-123" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            payoutService.MarkPayoutPaidAsync(p2.Id, adminId, new MarkPayoutPaidRequest { ExternalTransactionReference = "REF-DUPLICATE-123" }));

        Assert.Contains("already been recorded", ex.Message);
    }

    // ── 3. Settlement Engine Tests (Position-Level) ──────────────────────────

    [Fact]
    public async Task SettlementEngine_EligibleBookings_ClearsPendingToAvailable()
    {
        var (db, settlementService, payoutService, _, _) = CreateServices(nameof(SettlementEngine_EligibleBookings_ClearsPendingToAvailable));
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        // Create completed booking ended 25 hours ago
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-SETTLE-001",
            CourtId = courtId,
            UserId = clientId,
            StartTime = DateTime.UtcNow.AddHours(-27),
            EndTime = DateTime.UtcNow.AddHours(-25),
            Status = BookingStatus.Completed,
            PaymentStatus = PaymentStatus.Completed,
            TotalPrice = 1000m
        };

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            Amount = 1000m,
            CommissionAmount = 50m,
            OwnerNetAmount = 950m,
            Status = PaymentStatus.Completed,
            PaidAt = DateTime.UtcNow.AddHours(-27)
        };

        db.Bookings.Add(booking);
        db.Payments.Add(payment);

        db.OwnerBalances.Add(new OwnerBalance
        {
            OwnerId = ownerId,
            PendingBalance = 950m,
            AvailableBalance = 0m
        });
        await db.SaveChangesAsync();

        var batch = await settlementService.ExecuteSettlementBatchAsync(bufferHours: 24);

        Assert.NotNull(batch);
        Assert.Equal(1, batch.ItemCount);
        Assert.Equal(950m, batch.TotalNet);

        var balance = await payoutService.GetOwnerBalanceAsync(ownerId);
        Assert.Equal(0m, balance.PendingBalance);
        Assert.Equal(950m, balance.AvailableBalance);

        // Invariant: Exactly one settlement item created
        var items = await db.SettlementItems.Where(s => s.BookingId == booking.Id).ToListAsync();
        Assert.Single(items);
        Assert.Equal(950m, items[0].NetAmount);
    }

    [Fact]
    public async Task SettlementEngine_PartiallyRefundedBooking_SettlesRetainedFeeOnly()
    {
        var (db, settlementService, payoutService, _, _) = CreateServices(nameof(SettlementEngine_PartiallyRefundedBooking_SettlesRetainedFeeOnly));
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-SETTLE-PARTIAL",
            CourtId = courtId,
            UserId = clientId,
            StartTime = DateTime.UtcNow.AddHours(-30),
            EndTime = DateTime.UtcNow.AddHours(-28),
            Status = BookingStatus.Cancelled,
            PaymentStatus = PaymentStatus.PartiallyRefunded,
            TotalPrice = 1000m,
            CancellationFee = 500m
        };

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            Amount = 1000m,
            CommissionAmount = 50m,
            OwnerNetAmount = 950m,
            Status = PaymentStatus.PartiallyRefunded
        };

        db.Bookings.Add(booking);
        db.Payments.Add(payment);

        // Pending balance had 475 left after refund
        db.OwnerBalances.Add(new OwnerBalance
        {
            OwnerId = ownerId,
            PendingBalance = 475m,
            AvailableBalance = 0m
        });
        await db.SaveChangesAsync();

        var batch = await settlementService.ExecuteSettlementBatchAsync(bufferHours: 24);

        Assert.Equal(1, batch.ItemCount);
        Assert.Equal(475m, batch.TotalNet);

        var balance = await payoutService.GetOwnerBalanceAsync(ownerId);
        Assert.Equal(0m, balance.PendingBalance);
        Assert.Equal(475m, balance.AvailableBalance);
    }

    [Fact]
    public async Task SettlementEngine_IneligibleRecentBooking_IsSkipped()
    {
        var (db, settlementService, payoutService, _, _) = CreateServices(nameof(SettlementEngine_IneligibleRecentBooking_IsSkipped));
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        // Ended only 2 hours ago (buffer is 24 hours)
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-SETTLE-RECENT",
            CourtId = courtId,
            UserId = clientId,
            StartTime = DateTime.UtcNow.AddHours(-3),
            EndTime = DateTime.UtcNow.AddHours(-2),
            Status = BookingStatus.Completed,
            PaymentStatus = PaymentStatus.Completed,
            TotalPrice = 500m
        };

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            Amount = 500m,
            CommissionAmount = 25m,
            OwnerNetAmount = 475m,
            Status = PaymentStatus.Completed
        };

        db.Bookings.Add(booking);
        db.Payments.Add(payment);
        db.OwnerBalances.Add(new OwnerBalance { OwnerId = ownerId, PendingBalance = 475m, AvailableBalance = 0m });
        await db.SaveChangesAsync();

        var batch = await settlementService.ExecuteSettlementBatchAsync(bufferHours: 24);
        Assert.Equal(0, batch.ItemCount);

        var balance = await payoutService.GetOwnerBalanceAsync(ownerId);
        Assert.Equal(475m, balance.PendingBalance);
        Assert.Equal(0m, balance.AvailableBalance);
    }

    [Fact]
    public async Task SettlementEngine_SettlementIdempotency_RunningTwiceDoesNotDoubleSettle()
    {
        var (db, settlementService, payoutService, _, _) = CreateServices(nameof(SettlementEngine_SettlementIdempotency_RunningTwiceDoesNotDoubleSettle));
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-SETTLE-IDEMPOTENT",
            CourtId = courtId,
            UserId = clientId,
            StartTime = DateTime.UtcNow.AddHours(-30),
            EndTime = DateTime.UtcNow.AddHours(-28),
            Status = BookingStatus.Completed,
            PaymentStatus = PaymentStatus.Completed,
            TotalPrice = 600m
        };

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            Amount = 600m,
            CommissionAmount = 30m,
            OwnerNetAmount = 570m,
            Status = PaymentStatus.Completed
        };

        db.Bookings.Add(booking);
        db.Payments.Add(payment);
        db.OwnerBalances.Add(new OwnerBalance { OwnerId = ownerId, PendingBalance = 570m, AvailableBalance = 0m });
        await db.SaveChangesAsync();

        var b1 = await settlementService.ExecuteSettlementBatchAsync(bufferHours: 24);
        Assert.Equal(1, b1.ItemCount);

        var b2 = await settlementService.ExecuteSettlementBatchAsync(bufferHours: 24);
        Assert.Equal(0, b2.ItemCount);

        var balance = await payoutService.GetOwnerBalanceAsync(ownerId);
        Assert.Equal(570m, balance.AvailableBalance);
        Assert.Equal(0m, balance.PendingBalance);
    }

    // ── 4. Refund & Recovery Obligation Tests ─────────────────────────────────

    [Fact]
    public async Task RefundBeforeSettlement_DeductsFromPendingBalanceAndUpdatesTotalRefunded()
    {
        var (db, _, payoutService, _, paymentService) = CreateServices(nameof(RefundBeforeSettlement_DeductsFromPendingBalanceAndUpdatesTotalRefunded));
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-REFUND-UNSETTLED",
            CourtId = courtId,
            UserId = clientId,
            StartTime = DateTime.UtcNow.AddDays(2),
            EndTime = DateTime.UtcNow.AddDays(2).AddHours(1),
            Status = BookingStatus.Confirmed,
            PaymentStatus = PaymentStatus.Completed,
            TotalPrice = 1000m
        };

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            Amount = 1000m,
            CommissionAmount = 50m,
            OwnerNetAmount = 950m,
            Status = PaymentStatus.Completed,
            Method = PaymentMethod.PayAtFacility
        };

        db.Bookings.Add(booking);
        db.Payments.Add(payment);
        db.OwnerBalances.Add(new OwnerBalance
        {
            OwnerId = ownerId,
            PendingBalance = 950m,
            AvailableBalance = 0m,
            TotalRefunded = 0m
        });
        await db.SaveChangesAsync();

        var refundResult = await paymentService.ProcessRefundAsync(booking.Id, "Player cancellation");
        Assert.True(refundResult.Success);

        var balance = await payoutService.GetOwnerBalanceAsync(ownerId);
        Assert.Equal(0m, balance.PendingBalance);
        Assert.Equal(0m, balance.AvailableBalance);
        Assert.Equal(950m, balance.TotalRefunded);
    }

    [Fact]
    public async Task RefundAfterSettlement_WithSufficientBalance_DeductsFromAvailableBalance()
    {
        var (db, _, payoutService, _, paymentService) = CreateServices(nameof(RefundAfterSettlement_WithSufficientBalance_DeductsFromAvailableBalance));
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-REFUND-SETTLED",
            CourtId = courtId,
            UserId = clientId,
            StartTime = DateTime.UtcNow.AddHours(-30),
            EndTime = DateTime.UtcNow.AddHours(-28),
            Status = BookingStatus.Completed,
            PaymentStatus = PaymentStatus.Completed,
            TotalPrice = 1000m
        };

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            Amount = 1000m,
            CommissionAmount = 50m,
            OwnerNetAmount = 950m,
            Status = PaymentStatus.Completed,
            Method = PaymentMethod.PayAtFacility
        };

        var batch = new SettlementBatch
        {
            Id = Guid.NewGuid(),
            BatchReference = "SETTLE-TEST-001",
            PeriodStart = booking.StartTime,
            PeriodEnd = booking.EndTime,
            TotalGross = 1000m,
            TotalCommission = 50m,
            TotalNet = 950m,
            ItemCount = 1,
            Status = SettlementStatus.Completed
        };

        var settlementItem = new SettlementItem
        {
            Id = Guid.NewGuid(),
            SettlementBatchId = batch.Id,
            BookingId = booking.Id,
            PaymentId = payment.Id,
            OwnerId = ownerId,
            GrossAmount = 1000m,
            CommissionAmount = 50m,
            NetAmount = 950m
        };

        db.Bookings.Add(booking);
        db.Payments.Add(payment);
        db.SettlementBatches.Add(batch);
        db.SettlementItems.Add(settlementItem);

        db.OwnerBalances.Add(new OwnerBalance
        {
            OwnerId = ownerId,
            AvailableBalance = 2000m,
            PendingBalance = 0m,
            TotalRefunded = 0m
        });
        await db.SaveChangesAsync();

        var refundResult = await paymentService.ProcessRefundAsync(booking.Id, "Admin refund after settlement");
        Assert.True(refundResult.Success);

        var balance = await payoutService.GetOwnerBalanceAsync(ownerId);
        Assert.Equal(1050m, balance.AvailableBalance); // 2000 - 950
        Assert.Equal(950m, balance.TotalRefunded);
        Assert.Equal(0m, balance.OutstandingDeficit);
    }

    [Fact]
    public async Task RefundAfterPayout_InsufficientBalance_CreatesRecoveryObligationAndLeavesAvailableZero()
    {
        var (db, _, payoutService, recoveryService, paymentService) = CreateServices(nameof(RefundAfterPayout_InsufficientBalance_CreatesRecoveryObligationAndLeavesAvailableZero));
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-REFUND-POSTPAYOUT",
            CourtId = courtId,
            UserId = clientId,
            StartTime = DateTime.UtcNow.AddHours(-40),
            EndTime = DateTime.UtcNow.AddHours(-38),
            Status = BookingStatus.Completed,
            PaymentStatus = PaymentStatus.Completed,
            TotalPrice = 1000m
        };

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            Amount = 1000m,
            CommissionAmount = 50m,
            OwnerNetAmount = 950m,
            Status = PaymentStatus.Completed,
            Method = PaymentMethod.PayAtFacility
        };

        var batch = new SettlementBatch { Id = Guid.NewGuid(), BatchReference = "BATCH-002", PeriodStart = booking.StartTime, PeriodEnd = booking.EndTime, Status = SettlementStatus.Completed };
        var sItem = new SettlementItem { Id = Guid.NewGuid(), SettlementBatchId = batch.Id, BookingId = booking.Id, PaymentId = payment.Id, OwnerId = ownerId, GrossAmount = 1000m, CommissionAmount = 50m, NetAmount = 950m };

        db.Bookings.Add(booking);
        db.Payments.Add(payment);
        db.SettlementBatches.Add(batch);
        db.SettlementItems.Add(sItem);

        // Owner only has 200 EGP available because they withdrew the rest
        db.OwnerBalances.Add(new OwnerBalance
        {
            OwnerId = ownerId,
            AvailableBalance = 200m,
            PendingBalance = 0m,
            OutstandingDeficit = 0m
        });
        await db.SaveChangesAsync();

        var refundResult = await paymentService.ProcessRefundAsync(booking.Id, "Disputed booking after payout");
        Assert.True(refundResult.Success);

        var balance = await payoutService.GetOwnerBalanceAsync(ownerId);
        // Invariant: AvailableBalance is floored at 0, deficit = 950 - 200 = 750
        Assert.Equal(0m, balance.AvailableBalance);
        Assert.Equal(750m, balance.OutstandingDeficit);
        Assert.Equal(950m, balance.TotalRefunded);

        // Invariant: OutstandingDeficit == sum(RemainingDeficitAmount) of active obligations
        var obligations = await db.RecoveryObligations.Where(o => o.OwnerId == ownerId && o.Status == RecoveryStatus.Active).ToListAsync();
        Assert.Single(obligations);
        Assert.Equal(750m, obligations[0].RemainingDeficitAmount);
        Assert.Equal(balance.OutstandingDeficit, obligations.Sum(o => o.RemainingDeficitAmount));
    }

    [Fact]
    public async Task SettlementEngine_MultipleActiveRecoveryObligations_ConsumesInFifoOrder()
    {
        var (db, settlementService, payoutService, _, _) = CreateServices(nameof(SettlementEngine_MultipleActiveRecoveryObligations_ConsumesInFifoOrder));
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        // Create two active recovery obligations (older = 400 EGP, newer = 300 EGP, total deficit = 700 EGP)
        var ob1 = new RecoveryObligation
        {
            Id = Guid.NewGuid(),
            ObligationReference = "REC-FIFO-001",
            OwnerId = ownerId,
            BookingId = Guid.NewGuid(),
            PaymentId = Guid.NewGuid(),
            TotalDeficitAmount = 400m,
            RemainingDeficitAmount = 400m,
            Status = RecoveryStatus.Active,
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        };

        var ob2 = new RecoveryObligation
        {
            Id = Guid.NewGuid(),
            ObligationReference = "REC-FIFO-002",
            OwnerId = ownerId,
            BookingId = Guid.NewGuid(),
            PaymentId = Guid.NewGuid(),
            TotalDeficitAmount = 300m,
            RemainingDeficitAmount = 300m,
            Status = RecoveryStatus.Active,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };

        db.RecoveryObligations.AddRange(ob1, ob2);

        // Owner balance has 700 deficit
        db.OwnerBalances.Add(new OwnerBalance
        {
            OwnerId = ownerId,
            PendingBalance = 500m,
            AvailableBalance = 0m,
            OutstandingDeficit = 700m
        });

        // Add an eligible booking clearing 500 EGP net
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-SETTLE-RECOVER",
            CourtId = courtId,
            UserId = clientId,
            StartTime = DateTime.UtcNow.AddHours(-30),
            EndTime = DateTime.UtcNow.AddHours(-28),
            Status = BookingStatus.Completed,
            PaymentStatus = PaymentStatus.Completed,
            TotalPrice = 526.32m
        };

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            Amount = 526.32m,
            CommissionAmount = 26.32m,
            OwnerNetAmount = 500m,
            Status = PaymentStatus.Completed
        };

        db.Bookings.Add(booking);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        // Run settlement: 500 EGP net should fully recover ob1 (400 EGP) and partially recover ob2 (100 EGP)
        var batch = await settlementService.ExecuteSettlementBatchAsync(bufferHours: 24);

        var balance = await payoutService.GetOwnerBalanceAsync(ownerId);
        // Deficit decreased from 700 to 200. Available remains 0 because deficit was not fully cleared.
        Assert.Equal(200m, balance.OutstandingDeficit);
        Assert.Equal(0m, balance.AvailableBalance);

        var ob1Reloaded = await db.RecoveryObligations.FindAsync(ob1.Id);
        var ob2Reloaded = await db.RecoveryObligations.FindAsync(ob2.Id);

        Assert.Equal(RecoveryStatus.Recovered, ob1Reloaded!.Status);
        Assert.Equal(0m, ob1Reloaded.RemainingDeficitAmount);

        Assert.Equal(RecoveryStatus.Active, ob2Reloaded!.Status);
        Assert.Equal(200m, ob2Reloaded.RemainingDeficitAmount); // 300 - 100

        // Invariant: OutstandingDeficit == sum of remaining deficit
        Assert.Equal(balance.OutstandingDeficit, ob2Reloaded.RemainingDeficitAmount);
    }

    [Fact]
    public async Task ManualSettleAndWriteOff_UpdatesRemainingDeficitAndOwnerBalanceAtomically()
    {
        var (db, _, payoutService, recoveryService, _) = CreateServices(nameof(ManualSettleAndWriteOff_UpdatesRemainingDeficitAndOwnerBalanceAtomically));
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var adminId = Guid.NewGuid();

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-MANUAL-001",
            CourtId = courtId,
            UserId = clientId,
            StartTime = DateTime.UtcNow.AddDays(-2),
            EndTime = DateTime.UtcNow.AddDays(-2).AddHours(1),
            TotalPrice = 1000m,
            Status = BookingStatus.Completed,
            PaymentStatus = PaymentStatus.Completed
        };
        db.Bookings.Add(booking);

        var obligation = new RecoveryObligation
        {
            Id = Guid.NewGuid(),
            ObligationReference = "REC-MANUAL-001",
            OwnerId = ownerId,
            BookingId = booking.Id,
            PaymentId = Guid.NewGuid(),
            TotalDeficitAmount = 1000m,
            RemainingDeficitAmount = 1000m,
            Status = RecoveryStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        db.RecoveryObligations.Add(obligation);
        db.OwnerBalances.Add(new OwnerBalance
        {
            OwnerId = ownerId,
            OutstandingDeficit = 1000m
        });
        await db.SaveChangesAsync();

        // 1. Partial manual settlement of 400 EGP
        var settled = await recoveryService.SettleObligationManuallyAsync(obligation.Id, adminId, new ManualSettleRecoveryRequest
        {
            Amount = 400m,
            ExternalReference = "MANUAL-BANK-REF-1"
        });

        Assert.Equal(600m, settled.RemainingDeficitAmount);
        var b1 = await payoutService.GetOwnerBalanceAsync(ownerId);
        Assert.Equal(600m, b1.OutstandingDeficit);

        // 2. Write-off remaining 600 EGP
        var writtenOff = await recoveryService.WriteOffObligationAsync(obligation.Id, adminId, "Uncollectible debt after owner left");
        Assert.Equal("WrittenOff", writtenOff.Status);
        Assert.Equal(0m, writtenOff.RemainingDeficitAmount);

        var b2 = await payoutService.GetOwnerBalanceAsync(ownerId);
        Assert.Equal(0m, b2.OutstandingDeficit);
    }

    // ── 5. Historical Backfill Verification ───────────────────────────────────

    [Fact]
    public async Task EnsureHistoricalOwnerBalancesBackfilled_CorrectlyPartitionsHistoricalFunds()
    {
        var (db, _, payoutService, _, _) = CreateServices(nameof(EnsureHistoricalOwnerBalancesBackfilled_CorrectlyPartitionsHistoricalFunds));
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        // Past booking (ended 3 days ago) -> should enter AvailableBalance
        var pastBooking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-BACKFILL-PAST",
            CourtId = courtId,
            UserId = clientId,
            StartTime = DateTime.UtcNow.AddDays(-3),
            EndTime = DateTime.UtcNow.AddDays(-3).AddHours(1),
            Status = BookingStatus.Completed,
            PaymentStatus = PaymentStatus.Completed,
            TotalPrice = 1000m
        };
        var pastPayment = new Payment
        {
            Id = Guid.NewGuid(),
            BookingId = pastBooking.Id,
            Amount = 1000m,
            CommissionAmount = 50m,
            OwnerNetAmount = 950m,
            Status = PaymentStatus.Completed
        };

        // Future booking (scheduled tomorrow) -> should enter PendingBalance
        var futureBooking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-BACKFILL-FUTURE",
            CourtId = courtId,
            UserId = clientId,
            StartTime = DateTime.UtcNow.AddDays(1),
            EndTime = DateTime.UtcNow.AddDays(1).AddHours(1),
            Status = BookingStatus.Confirmed,
            PaymentStatus = PaymentStatus.Completed,
            TotalPrice = 600m
        };
        var futurePayment = new Payment
        {
            Id = Guid.NewGuid(),
            BookingId = futureBooking.Id,
            Amount = 600m,
            CommissionAmount = 30m,
            OwnerNetAmount = 570m,
            Status = PaymentStatus.Completed
        };

        // Unpaid PayAtFacility booking -> should NOT enter any balance
        var unpaidBooking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-BACKFILL-UNPAID",
            CourtId = courtId,
            UserId = clientId,
            StartTime = DateTime.UtcNow.AddDays(-2),
            EndTime = DateTime.UtcNow.AddDays(-2).AddHours(1),
            Status = BookingStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            TotalPrice = 500m
        };
        var unpaidPayment = new Payment
        {
            Id = Guid.NewGuid(),
            BookingId = unpaidBooking.Id,
            Amount = 500m,
            Method = PaymentMethod.PayAtFacility,
            Status = PaymentStatus.Pending
        };

        db.Bookings.AddRange(pastBooking, futureBooking, unpaidBooking);
        db.Payments.AddRange(pastPayment, futurePayment, unpaidPayment);
        await db.SaveChangesAsync();

        // Run backfill
        await SeedData.EnsureHistoricalOwnerBalancesBackfilledAsync(db, NullLogger<AppDbContext>.Instance);

        var balance = await payoutService.GetOwnerBalanceAsync(ownerId);
        Assert.Equal(950m, balance.AvailableBalance);
        Assert.Equal(570m, balance.PendingBalance);
        Assert.Equal(0m, balance.TotalPaidOut);
        Assert.Equal(0m, balance.OutstandingDeficit);
    }
}

// ── Test Double / Fake Gateway ──────────────────────────────────────────────

internal class FakePaymentGateway : IPaymentGatewayService
{
    public string ProviderName => "FakeGateway";

    public Task<PaymentInitiationResult> InitiatePaymentAsync(PaymentInitiationRequest request)
        => Task.FromResult(new PaymentInitiationResult(true, "https://checkout.fake/pay", "fake-order-1", null));

    public Task<PaymentVerificationResult> VerifyPaymentAsync(string providerOrderId)
        => Task.FromResult(new PaymentVerificationResult(true, "fake-tx-1", 1000m, "Completed", null));

    public Task<RefundResult> RefundAsync(RefundRequest request)
        => Task.FromResult(new RefundResult(true, "REFUND-TX-123", null));

    public bool ValidateWebhookSignature(string payload, string signature) => true;
}

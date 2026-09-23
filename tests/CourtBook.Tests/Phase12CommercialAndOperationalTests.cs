using CourtBook.Application.DTOs;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CourtBook.Tests;

public class Phase12CommercialAndOperationalTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // 1. PROMO CODE SERVICE TESTS
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PromoCodeService_ValidatesPercentageCode_Correctly()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var promoService = new PromoCodeService(db);

        var promo = new PromoCode
        {
            Id = Guid.NewGuid(),
            Code = "SAVE20",
            DiscountType = DiscountType.Percentage,
            Value = 20,
            IsActive = true
        };
        db.PromoCodes.Add(promo);
        await db.SaveChangesAsync();

        var result = await promoService.ValidatePromoCodeAsync(new ValidatePromoCodeRequest
        {
            Code = "save20", // Case-insensitive
            BookingAmount = 300m
        });

        Assert.True(result.IsValid);
        Assert.Equal(60m, result.DiscountAmount); // 20% of 300 = 60
        Assert.Equal(240m, result.FinalAmount);
        Assert.Equal(promo.Id, result.PromoCodeId);
    }

    [Fact]
    public async Task PromoCodeService_ValidatesFixedAmountCode_Correctly()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var promoService = new PromoCodeService(db);

        var promo = new PromoCode
        {
            Id = Guid.NewGuid(),
            Code = "FLAT50",
            DiscountType = DiscountType.FixedAmount,
            Value = 50,
            IsActive = true
        };
        db.PromoCodes.Add(promo);
        await db.SaveChangesAsync();

        var result = await promoService.ValidatePromoCodeAsync(new ValidatePromoCodeRequest
        {
            Code = "FLAT50",
            BookingAmount = 250m
        });

        Assert.True(result.IsValid);
        Assert.Equal(50m, result.DiscountAmount);
        Assert.Equal(200m, result.FinalAmount);
    }

    [Fact]
    public async Task PromoCodeService_AppliesMaxDiscountCap_WhenPercentageExceeds()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var promoService = new PromoCodeService(db);

        var promo = new PromoCode
        {
            Id = Guid.NewGuid(),
            Code = "HALFPRICE",
            DiscountType = DiscountType.Percentage,
            Value = 50,
            MaxDiscountAmount = 100m, // Max cap 100
            IsActive = true
        };
        db.PromoCodes.Add(promo);
        await db.SaveChangesAsync();

        var result = await promoService.ValidatePromoCodeAsync(new ValidatePromoCodeRequest
        {
            Code = "HALFPRICE",
            BookingAmount = 500m // 50% would be 250, but capped at 100
        });

        Assert.True(result.IsValid);
        Assert.Equal(100m, result.DiscountAmount);
        Assert.Equal(400m, result.FinalAmount);
    }

    [Fact]
    public async Task PromoCodeService_RejectsExpiredCode()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var promoService = new PromoCodeService(db);

        var promo = new PromoCode
        {
            Id = Guid.NewGuid(),
            Code = "EXPIRED",
            DiscountType = DiscountType.Percentage,
            Value = 10,
            ValidTo = DateTime.UtcNow.AddDays(-1), // Expired yesterday
            IsActive = true
        };
        db.PromoCodes.Add(promo);
        await db.SaveChangesAsync();

        var result = await promoService.ValidatePromoCodeAsync(new ValidatePromoCodeRequest
        {
            Code = "EXPIRED",
            BookingAmount = 200m
        });

        Assert.False(result.IsValid);
        Assert.Contains("expired", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PromoCodeService_RejectsWhenBelowMinimumBookingAmount()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var promoService = new PromoCodeService(db);

        var promo = new PromoCode
        {
            Id = Guid.NewGuid(),
            Code = "VIP500",
            DiscountType = DiscountType.FixedAmount,
            Value = 100,
            MinBookingAmount = 500m,
            IsActive = true
        };
        db.PromoCodes.Add(promo);
        await db.SaveChangesAsync();

        var result = await promoService.ValidatePromoCodeAsync(new ValidatePromoCodeRequest
        {
            Code = "VIP500",
            BookingAmount = 300m // Less than 500
        });

        Assert.False(result.IsValid);
        Assert.Contains("500", result.ErrorMessage);
    }

    [Fact]
    public async Task PromoCodeService_RejectsWhenMaxUsagesExceeded()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var promoService = new PromoCodeService(db);

        var promo = new PromoCode
        {
            Id = Guid.NewGuid(),
            Code = "MAXED",
            DiscountType = DiscountType.Percentage,
            Value = 15,
            MaxUsages = 3,
            UsageCount = 3, // Already used 3 times
            IsActive = true
        };
        db.PromoCodes.Add(promo);
        await db.SaveChangesAsync();

        var result = await promoService.ValidatePromoCodeAsync(new ValidatePromoCodeRequest
        {
            Code = "MAXED",
            BookingAmount = 200m
        });

        Assert.False(result.IsValid);
        Assert.Contains("limit", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PromoCodeService_RejectsInactiveCode()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var promoService = new PromoCodeService(db);

        var promo = new PromoCode
        {
            Id = Guid.NewGuid(),
            Code = "DISABLED",
            DiscountType = DiscountType.Percentage,
            Value = 15,
            IsActive = false
        };
        db.PromoCodes.Add(promo);
        await db.SaveChangesAsync();

        var result = await promoService.ValidatePromoCodeAsync(new ValidatePromoCodeRequest
        {
            Code = "DISABLED",
            BookingAmount = 200m
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task PromoCodeService_CreateAndDeactivate_WorksCorrectly()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var promoService = new PromoCodeService(db);

        var created = await promoService.CreatePromoCodeAsync(new CreatePromoCodeRequest
        {
            Code = "WELCOME10",
            DiscountType = DiscountType.Percentage,
            Value = 10,
            IsActive = true
        });

        Assert.NotNull(created);
        Assert.Equal("WELCOME10", created.Code);
        Assert.True(created.IsActive);

        var deactivated = await promoService.DeactivatePromoCodeAsync(created.Id);
        Assert.True(deactivated);

        var inDb = await db.PromoCodes.FindAsync(created.Id);
        Assert.NotNull(inDb);
        Assert.False(inDb.IsActive);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 2. COURT ADDON SERVICE TESTS
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CourtAddonService_CreateAndRetrieveAddons_SucceedsForOwner()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, ownerId, _) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var addonService = new CourtAddonService(db);

        var created = await addonService.CreateCourtAddonAsync(ownerId, new CreateCourtAddonDto
        {
            CourtId = courtId,
            Name = "Padel Racket Pro",
            NameAr = "مضرب بادل احترافي",
            Price = 60m,
            Unit = "Racket",
            IsAvailable = true
        });

        Assert.NotNull(created);
        Assert.Equal("Padel Racket Pro", created.Name);
        Assert.Equal(60m, created.Price);

        var list = await addonService.GetAddonsByCourtIdAsync(courtId, onlyAvailable: true);
        Assert.Single(list);
        Assert.Equal("Padel Racket Pro", list[0].Name);
    }

    [Fact]
    public async Task CourtAddonService_ThrowsUnauthorized_WhenNonOwnerAttemptsCreate()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, courtId, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var addonService = new CourtAddonService(db);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            addonService.CreateCourtAddonAsync(clientId, new CreateCourtAddonDto
            {
                CourtId = courtId,
                Name = "Hacker Racket",
                Price = 10m
            }));
    }

    [Fact]
    public async Task CourtAddonService_DeleteAddon_SucceedsForOwner()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, courtId, ownerId, _) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var addonService = new CourtAddonService(db);

        var created = await addonService.CreateCourtAddonAsync(ownerId, new CreateCourtAddonDto
        {
            CourtId = courtId,
            Name = "Training Bibs",
            Price = 25m
        });

        var deleted = await addonService.DeleteCourtAddonAsync(ownerId, created.Id);
        Assert.True(deleted);

        var list = await addonService.GetAddonsByCourtIdAsync(courtId, onlyAvailable: false);
        Assert.Empty(list);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 3. OWNER OPERATIONAL FEATURES (MANUAL BOOKING & QUICK CHECK-IN)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task OwnerService_CreateManualBooking_SucceedsWithCustomPriceAndPhoneDetails()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, courtId, ownerId, _) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var ownerService = new OwnerService(db);

        var startTime = DateTime.UtcNow.Date.AddDays(2).AddHours(18);
        var endTime = startTime.AddHours(1);

        var request = new CreateManualBookingRequest
        {
            CourtId = courtId,
            StartTime = startTime,
            EndTime = endTime,
            CustomerName = "Captain Mostafa",
            CustomerPhone = "01099887766",
            CustomPrice = 350m,
            Notes = "VIP Phone reservation"
        };

        var response = await ownerService.CreateManualBookingAsync(ownerId, request);

        Assert.NotNull(response);
        Assert.True(response.IsManualBooking);
        Assert.Equal("Captain Mostafa", response.CustomerName);
        Assert.Equal("01099887766", response.CustomerPhone);
        Assert.Equal(350m, response.TotalPrice);
        Assert.Equal("Confirmed", response.Status);
        Assert.Equal("Completed", response.PaymentStatus);
        Assert.False(response.IsCheckedIn);

        // Verify persisted entity in database
        var inDb = await db.Bookings.FindAsync(response.Id);
        Assert.NotNull(inDb);
        Assert.True(inDb.IsManualBooking);
        Assert.Equal("Captain Mostafa", inDb.CustomerName);
        Assert.Equal("01099887766", inDb.CustomerPhone);
        Assert.Equal(350m, inDb.TotalPrice);
    }

    [Fact]
    public async Task OwnerService_CreateManualBooking_DefaultsToCourtStandardPrice_WhenCustomPriceNotProvided()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, courtId, ownerId, _) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var ownerService = new OwnerService(db);

        var startTime = DateTime.UtcNow.Date.AddDays(2).AddHours(14);
        var endTime = startTime.AddHours(1);

        var request = new CreateManualBookingRequest
        {
            CourtId = courtId,
            StartTime = startTime,
            EndTime = endTime,
            CustomerName = "Tarek Phone",
            CustomerPhone = "01234567890",
            CustomPrice = null
        };

        var response = await ownerService.CreateManualBookingAsync(ownerId, request);

        Assert.NotNull(response);
        Assert.Equal(200m, response.TotalPrice); // Standard court rate is 200/hr
    }

    [Fact]
    public async Task OwnerService_CreateManualBooking_ThrowsUnauthorized_WhenNonOwnerAttempts()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, courtId, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var ownerService = new OwnerService(db);

        var request = new CreateManualBookingRequest
        {
            CourtId = courtId,
            StartTime = DateTime.UtcNow.Date.AddDays(1).AddHours(10),
            EndTime = DateTime.UtcNow.Date.AddDays(1).AddHours(11),
            CustomerName = "Intruder",
            CustomerPhone = "0000000000"
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ownerService.CreateManualBookingAsync(clientId, request));
    }

    [Fact]
    public async Task OwnerService_QuickCheckIn_SucceedsForConfirmedBooking()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var ownerService = new OwnerService(db);

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            CourtId = courtId,
            UserId = clientId,
            StartTime = DateTime.UtcNow.Date.AddDays(1).AddHours(19),
            EndTime = DateTime.UtcNow.Date.AddDays(1).AddHours(20),
            TotalPrice = 200m,
            Status = BookingStatus.Confirmed,
            PaymentStatus = PaymentStatus.Completed,
            BookingReference = "CB-CHECKIN-TEST-01",
            IsCheckedIn = false,
            CreatedAt = DateTime.UtcNow
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var checkInResult = await ownerService.QuickCheckInAsync(ownerId, new QuickCheckInRequest
        {
            BookingReference = "cb-checkin-test-01" // Case insensitive
        });

        Assert.True(checkInResult.Success);
        Assert.NotNull(checkInResult.Booking);
        Assert.True(checkInResult.Booking.IsCheckedIn);
        Assert.NotNull(checkInResult.Booking.CheckedInAt);

        // Verify database persistence
        var inDb = await db.Bookings.FindAsync(booking.Id);
        Assert.NotNull(inDb);
        Assert.True(inDb.IsCheckedIn);
        Assert.NotNull(inDb.CheckedInAt);
    }

    [Fact]
    public async Task OwnerService_QuickCheckIn_HandlesAlreadyCheckedIn_Idempotently()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var ownerService = new OwnerService(db);

        var initialTime = DateTime.UtcNow.AddMinutes(-10);
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            CourtId = courtId,
            UserId = clientId,
            StartTime = DateTime.UtcNow.Date.AddDays(1).AddHours(19),
            EndTime = DateTime.UtcNow.Date.AddDays(1).AddHours(20),
            TotalPrice = 200m,
            Status = BookingStatus.Confirmed,
            PaymentStatus = PaymentStatus.Completed,
            BookingReference = "CB-ALREADY-02",
            IsCheckedIn = true,
            CheckedInAt = initialTime,
            CreatedAt = DateTime.UtcNow
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var checkInResult = await ownerService.QuickCheckInAsync(ownerId, new QuickCheckInRequest
        {
            BookingReference = "CB-ALREADY-02"
        });

        Assert.True(checkInResult.Success);
        Assert.Contains("already", checkInResult.Message, StringComparison.OrdinalIgnoreCase);
        // Original timestamp should remain unchanged
        var inDb = await db.Bookings.FindAsync(booking.Id);
        Assert.NotNull(inDb);
        Assert.Equal(initialTime, inDb.CheckedInAt);
    }

    [Fact]
    public async Task OwnerService_QuickCheckIn_FailsForNonExistentReference()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, _, ownerId, _) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var ownerService = new OwnerService(db);

        var checkInResult = await ownerService.QuickCheckInAsync(ownerId, new QuickCheckInRequest
        {
            BookingReference = "NON-EXISTENT-REF"
        });

        Assert.False(checkInResult.Success);
    }

    [Fact]
    public async Task OwnerService_QuickCheckIn_FailsForCancelledBooking()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var ownerService = new OwnerService(db);

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            CourtId = courtId,
            UserId = clientId,
            StartTime = DateTime.UtcNow.Date.AddDays(1).AddHours(19),
            EndTime = DateTime.UtcNow.Date.AddDays(1).AddHours(20),
            TotalPrice = 200m,
            Status = BookingStatus.Cancelled,
            BookingReference = "CB-CANCELLED-03",
            IsCheckedIn = false,
            CreatedAt = DateTime.UtcNow
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var checkInResult = await ownerService.QuickCheckInAsync(ownerId, new QuickCheckInRequest
        {
            BookingReference = "CB-CANCELLED-03"
        });

        Assert.False(checkInResult.Success);
        Assert.Contains("cancelled", checkInResult.Message, StringComparison.OrdinalIgnoreCase);
    }
}

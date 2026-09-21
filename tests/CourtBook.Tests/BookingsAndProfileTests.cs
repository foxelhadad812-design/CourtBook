using CourtBook.Application.DTOs;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Services;
using Xunit;

namespace CourtBook.Tests;

public class BookingsAndProfileTests
{
    [Fact]
    public async Task GetByIdAsync_AllowsOwnerAndBooker_DeniesUnauthorizedUser()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, ownerId, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var bookingService = new BookingService(db);

        // Add client B
        var clientB = new User
        {
            Id = Guid.NewGuid(),
            Name = "Stranger Player",
            Email = "stranger@test.com",
            PasswordHash = "hash",
            Role = Role.Client
        };
        db.Users.Add(clientB);
        await db.SaveChangesAsync();

        // 14:00 is within 08:00 - 23:00 working hours
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
        var futureStart = DateTime.SpecifyKind(futureDate.ToDateTime(new TimeOnly(14, 0)), DateTimeKind.Utc);
        var futureEnd = futureStart.AddHours(1);

        var booking = await bookingService.CreateAsync(clientId, new CreateBookingRequest
        {
            CourtId = courtId,
            StartTime = futureStart,
            EndTime = futureEnd,
            Notes = "Test reservation"
        });

        // 1. Booker can view their own booking
        var ownBooking = await bookingService.GetByIdAsync(clientId, "Client", booking.Id);
        Assert.NotNull(ownBooking);
        Assert.Equal(booking.BookingReference, ownBooking.BookingReference);

        // 2. Venue Owner can view the booking
        var ownerBooking = await bookingService.GetByIdAsync(ownerId, "Owner", booking.Id);
        Assert.NotNull(ownerBooking);

        // 3. Client B (another user) is forbidden from viewing the booking
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            bookingService.GetByIdAsync(clientB.Id, "Client", booking.Id));
        Assert.Contains("permission", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelWithPolicy_PreventsUnauthorizedUser_FromCancelling()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, courtId, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var bookingService = new BookingService(db);

        var clientBId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = clientBId,
            Name = "Attacker",
            Email = "attacker@test.com",
            PasswordHash = "hash",
            Role = Role.Client
        });
        await db.SaveChangesAsync();

        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(4));
        var futureStart = DateTime.SpecifyKind(futureDate.ToDateTime(new TimeOnly(15, 0)), DateTimeKind.Utc);

        var booking = await bookingService.CreateAsync(clientId, new CreateBookingRequest
        {
            CourtId = courtId,
            StartTime = futureStart,
            EndTime = futureStart.AddHours(1)
        });

        // Client B tries to cancel Client A's booking
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            bookingService.CancelWithPolicyAsync(clientBId, "Client", booking.Id, null));
        Assert.Contains("permission", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancellationPolicy_CalculatesFullRefund_WhenInsideFreeWindow()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        // Configure Cancellation Policy directly
        db.CancellationPolicies.Add(new CancellationPolicy
        {
            Id = Guid.NewGuid(),
            VenueId = venueId,
            FreeCancellationHours = 24,
            LateCancellationFeePercent = 50m,
            PolicyDescription = "Free cancellation up to 24h."
        });
        await db.SaveChangesAsync();

        var bookingService = new BookingService(db);

        // Booking is 48 hours away (> 24 hours free window) at 16:00
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(48));
        var futureStart = DateTime.SpecifyKind(futureDate.ToDateTime(new TimeOnly(16, 0)), DateTimeKind.Utc);

        var booking = await bookingService.CreateAsync(clientId, new CreateBookingRequest
        {
            CourtId = courtId,
            StartTime = futureStart,
            EndTime = futureStart.AddHours(1)
        });

        // Preview
        var preview = await bookingService.GetCancellationPreviewAsync(clientId, "Client", booking.Id);
        Assert.True(preview.CanCancel);
        Assert.True(preview.IsFreeCancellation);
        Assert.Equal(0m, preview.CancellationFee);
        Assert.Equal(booking.TotalPrice, preview.RefundAmount);

        // Execute Cancellation
        var result = await bookingService.CancelWithPolicyAsync(clientId, "Client", booking.Id, new CancelBookingRequest { Reason = "Player changed plans" });
        Assert.True(result.Success);
        Assert.Equal(0m, result.CancellationFee);
        Assert.Equal(booking.TotalPrice, result.RefundAmount);

        var updated = await db.Bookings.FindAsync(booking.Id);
        Assert.Equal(BookingStatus.Cancelled, updated!.Status);
        Assert.NotNull(updated.CancelledAt);
        Assert.Equal("Player changed plans", updated.CancellationReason);
    }

    [Fact]
    public async Task CancellationPolicy_AppliesLateFee_WhenOutsideFreeWindow()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        db.CancellationPolicies.Add(new CancellationPolicy
        {
            Id = Guid.NewGuid(),
            VenueId = venueId,
            FreeCancellationHours = 24,
            LateCancellationFeePercent = 25m // 25% late fee
        });
        await db.SaveChangesAsync();

        var bookingService = new BookingService(db);

        // Create booking directly in DB to guarantee exact hours without time boundary restrictions
        var nearFutureStart = DateTime.UtcNow.AddHours(6);
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-NEAR-01",
            CourtId = courtId,
            UserId = clientId,
            StartTime = nearFutureStart,
            EndTime = nearFutureStart.AddHours(1),
            Status = BookingStatus.Confirmed,
            TotalPrice = 200m
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        // Preview
        var preview = await bookingService.GetCancellationPreviewAsync(clientId, "Client", booking.Id);
        Assert.True(preview.CanCancel);
        Assert.False(preview.IsFreeCancellation);

        // Price is 200m -> 25% fee = 50m, refund = 150m
        var expectedFee = Math.Round(booking.TotalPrice * 0.25m, 2);
        var expectedRefund = booking.TotalPrice - expectedFee;

        Assert.Equal(expectedFee, preview.CancellationFee);
        Assert.Equal(expectedRefund, preview.RefundAmount);

        // Execute Cancellation
        var result = await bookingService.CancelWithPolicyAsync(clientId, "Client", booking.Id, null);
        Assert.True(result.Success);
        Assert.Equal(expectedFee, result.CancellationFee);
        Assert.Equal(expectedRefund, result.RefundAmount);
    }

    [Fact]
    public async Task CancellationPolicy_RejectsCancellation_WhenAlreadyCancelledOrStarted()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, courtId, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var bookingService = new BookingService(db);

        var pastStart = DateTime.UtcNow.AddHours(-2);
        var pastEnd = pastStart.AddHours(1);

        // Manually add past booking
        var pastBooking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-PAST-01",
            CourtId = courtId,
            UserId = clientId,
            StartTime = pastStart,
            EndTime = pastEnd,
            Status = BookingStatus.Confirmed,
            TotalPrice = 200m
        };
        db.Bookings.Add(pastBooking);
        await db.SaveChangesAsync();

        // 1. Attempt cancelling past booking -> throws InvalidOperationException
        var exPast = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            bookingService.CancelWithPolicyAsync(clientId, "Client", pastBooking.Id, null));
        Assert.Contains("cannot be cancelled", exPast.Message, StringComparison.OrdinalIgnoreCase);

        // 2. Attempt cancelling already cancelled booking -> throws InvalidOperationException
        pastBooking.Status = BookingStatus.Cancelled;
        await db.SaveChangesAsync();

        var exAlready = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            bookingService.CancelWithPolicyAsync(clientId, "Client", pastBooking.Id, null));
        Assert.Contains("already cancelled", exAlready.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReviewEligibility_OnlyAllowsCompletedAndUnreviewedBookings()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var bookingService = new BookingService(db);

        var pastStart = DateTime.UtcNow.AddDays(-2);
        var pastBooking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-COMPL-01",
            CourtId = courtId,
            UserId = clientId,
            StartTime = pastStart,
            EndTime = pastStart.AddHours(1),
            Status = BookingStatus.Completed,
            TotalPrice = 200m
        };
        db.Bookings.Add(pastBooking);
        await db.SaveChangesAsync();

        // Before review: eligible
        var bookingDto = await bookingService.GetByIdAsync(clientId, "Client", pastBooking.Id);
        Assert.NotNull(bookingDto);
        Assert.True(bookingDto.IsEligibleForReview);
        Assert.False(bookingDto.HasReviewed);

        // Submit review
        db.Reviews.Add(new Review
        {
            Id = Guid.NewGuid(),
            BookingId = pastBooking.Id,
            VenueId = venueId,
            UserId = clientId,
            OverallRating = 5,
            CourtQualityRating = 5,
            CleanlinessRating = 5,
            StaffRating = 5,
            ValueRating = 5,
            Comment = "Excellent court!"
        });
        await db.SaveChangesAsync();

        // After review: ineligible
        var updatedDto = await bookingService.GetByIdAsync(clientId, "Client", pastBooking.Id);
        Assert.NotNull(updatedDto);
        Assert.False(updatedDto.IsEligibleForReview);
        Assert.True(updatedDto.HasReviewed);
    }

    [Fact]
    public async Task ProfileService_RetrievesAndUpdatesProfileAndPreferences()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, _, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var profileService = new ProfileService(db);

        // 1. Initial retrieval auto-provisions profile & preferences
        var profile = await profileService.GetProfileAsync(clientId);
        Assert.NotNull(profile);
        Assert.Equal("Omar Client", profile.Name);
        Assert.Equal("Beginner", profile.SkillLevel);
        Assert.NotNull(profile.Preferences);

        // 2. Update Profile & Preferences
        var updateRequest = new UpdateProfileRequest
        {
            Name = "Omar Al-Sayed",
            Phone = "01099887766",
            Bio = "Padel & Football enthusiast playing weekly in Maadi.",
            SkillLevel = "Advanced",
            PreferredSport = "Padel",
            PreferredCities = ["Cairo", "Giza"],
            PreferredDays = ["Thursday", "Friday"],
            PreferredTimeOfDay = ["Evening", "Night"]
        };

        var updated = await profileService.UpdateProfileAsync(clientId, updateRequest);
        Assert.NotNull(updated);
        Assert.Equal("Omar Al-Sayed", updated.Name);
        Assert.Equal("01099887766", updated.Phone);
        Assert.Equal("Advanced", updated.SkillLevel);
        Assert.Equal("Padel", updated.PreferredSport);
        Assert.Contains("Cairo", updated.Preferences.PreferredCities);
        Assert.Contains("Giza", updated.Preferences.PreferredCities);
        Assert.Contains("Friday", updated.Preferences.PreferredDays);

        // 3. Validation: Empty name throws ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            profileService.UpdateProfileAsync(clientId, new UpdateProfileRequest { Name = "   " }));
    }
}

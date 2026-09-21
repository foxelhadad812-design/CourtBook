using CourtBook.Application.DTOs;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Services;
using Xunit;

namespace CourtBook.Tests;

public class BookingFlowAndSlotSelectorTests
{
    [Fact]
    public async Task CourtService_GetByIdAsync_ReturnsDirectCourtDetails()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, _) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var courtService = new CourtService(db);

        var result = await courtService.GetByIdAsync(courtId);

        Assert.NotNull(result);
        Assert.Equal(courtId, result.Id);
        Assert.Equal(venueId, result.VenueId);
        Assert.Equal("Football Court 1", result.Name);
        Assert.Equal("Football", result.SportType);
        Assert.Equal(200m, result.PricePerHour);
    }

    [Fact]
    public async Task CourtService_GetByIdAsync_ReturnsNull_WhenCourtDoesNotExist()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var courtService = new CourtService(db);

        var result = await courtService.GetByIdAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Theory]
    [InlineData(60, 200, 15)]  // 60 min -> 1.0x price = 200, 15 slots between 08:00 and 23:00
    [InlineData(90, 300, 10)]  // 90 min -> 1.5x price = 300, 10 slots (15 hours / 1.5 = 10)
    [InlineData(120, 400, 7)]  // 120 min -> 2.0x price = 400, 7 slots (14 hours used, last 1 hour insufficient for 2h slot)
    public async Task AvailabilityService_CalculatesCorrectSlotsAndPrices_ForDifferentDurations(
        int durationMinutes, decimal expectedBasePrice, int expectedSlotCount)
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, courtId, _, _) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var availabilityService = new AvailabilityService(db);

        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
        var request = new CourtAvailabilityRequest
        {
            Date = futureDate,
            DurationMinutes = durationMinutes
        };

        var result = await availabilityService.GetCourtAvailabilityAsync(courtId, request);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(expectedSlotCount, result.Value.Slots.Count);

        // Check the first slot starting at 08:00
        var firstSlot = result.Value.Slots[0];
        Assert.Equal(new TimeOnly(8, 0), firstSlot.StartTime);
        Assert.Equal(SlotAvailabilityStatus.Available, firstSlot.Status);
        Assert.Equal(expectedBasePrice, firstSlot.BasePrice);
        Assert.Equal(expectedBasePrice, firstSlot.EffectivePrice); // Morning is standard rate
        Assert.False(firstSlot.IsPeak);
    }

    [Fact]
    public async Task AvailabilityService_CalculatesPeakPricing_ForExtendedDurations()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, courtId, _, _) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var availabilityService = new AvailabilityService(db);

        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(4));
        
        // 90 minutes slot: 18:00 to 19:30 overlaps with peak rule (18:00 to 22:00, 1.5x)
        var result90 = await availabilityService.GetCourtAvailabilityAsync(courtId, new CourtAvailabilityRequest
        {
            Date = futureDate,
            DurationMinutes = 90
        });

        Assert.True(result90.IsSuccess);
        // Base is 200 * 1.5 = 300. Peak multiplier is 1.5 -> Effective = 300 * 1.5 = 450
        var peakSlot = result90.Value!.Slots.FirstOrDefault(s => s.StartTime == new TimeOnly(18, 30) || s.StartTime == new TimeOnly(18, 0));
        Assert.NotNull(peakSlot);
        Assert.True(peakSlot.IsPeak);
        Assert.Equal(300m, peakSlot.BasePrice);
        Assert.Equal(450m, peakSlot.EffectivePrice);
    }

    [Fact]
    public async Task BookingService_DetectsOverlap_AndThrowsInvalidOperationException()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, courtId, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var bookingService = new BookingService(db);

        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var startTime = DateTime.SpecifyKind(futureDate.ToDateTime(new TimeOnly(16, 0)), DateTimeKind.Utc);
        var endTime = DateTime.SpecifyKind(futureDate.ToDateTime(new TimeOnly(17, 0)), DateTimeKind.Utc);

        // 1. Create initial confirmed booking
        var firstBooking = await bookingService.CreateAsync(clientId, new CreateBookingRequest
        {
            CourtId = courtId,
            StartTime = startTime,
            EndTime = endTime,
            Notes = "First booking"
        });

        Assert.NotNull(firstBooking);
        Assert.Equal("Confirmed", firstBooking.Status);

        // 2. Attempt second booking for overlapping slot (16:30 - 17:30)
        var overlapStartTime = DateTime.SpecifyKind(futureDate.ToDateTime(new TimeOnly(16, 30)), DateTimeKind.Utc);
        var overlapEndTime = DateTime.SpecifyKind(futureDate.ToDateTime(new TimeOnly(17, 30)), DateTimeKind.Utc);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => bookingService.CreateAsync(clientId, new CreateBookingRequest
        {
            CourtId = courtId,
            StartTime = overlapStartTime,
            EndTime = overlapEndTime,
            Notes = "Conflicting booking"
        }));

        Assert.Contains("Court is not available for the selected time slot", ex.Message);
    }
}

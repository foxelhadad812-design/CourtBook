using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Services;
using Xunit;

namespace CourtBook.Tests;

public class AvailabilityEngineTests
{
    [Fact]
    public async Task GetCourtAvailability_GeneratesCorrectSlotsAndAppliesPeakPriceRules()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, courtId, _, _) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var service = new AvailabilityService(db);

        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
        var request = new CourtAvailabilityRequest
        {
            Date = futureDate,
            DurationMinutes = 60
        };

        var result = await service.GetCourtAvailabilityAsync(courtId, request);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(futureDate, result.Value.Date);

        // Schedule is 08:00 to 23:00 -> 15 hours = 15 slots
        Assert.Equal(15, result.Value.Slots.Count);

        // Check standard morning slot (09:00 - 10:00): base price 200, effective 200, isPeak = false
        var morningSlot = result.Value.Slots.First(s => s.StartTime == new TimeOnly(9, 0));
        Assert.Equal(SlotAvailabilityStatus.Available, morningSlot.Status);
        Assert.Equal(200m, morningSlot.BasePrice);
        Assert.Equal(200m, morningSlot.EffectivePrice);
        Assert.False(morningSlot.IsPeak);

        // Check evening peak slot (19:00 - 20:00): base price 200, effective price 300 (1.5x multiplier), isPeak = true
        var peakSlot = result.Value.Slots.First(s => s.StartTime == new TimeOnly(19, 0));
        Assert.Equal(SlotAvailabilityStatus.Available, peakSlot.Status);
        Assert.Equal(200m, peakSlot.BasePrice);
        Assert.Equal(300m, peakSlot.EffectivePrice);
        Assert.True(peakSlot.IsPeak);
        Assert.Equal("Peak Evening", peakSlot.AppliedRuleName);
    }

    [Fact]
    public async Task GetCourtAvailability_MarksBookedSlotsCorrectly()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, courtId, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var service = new AvailabilityService(db);

        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        
        // Add existing confirmed booking at 14:00 - 15:00 on futureDate (in Egypt local time)
        var startUtc = TimeZoneHelper.CreateUtcFromEgyptDateAndTime(futureDate, new TimeOnly(14, 0));
        var endUtc = TimeZoneHelper.CreateUtcFromEgyptDateAndTime(futureDate, new TimeOnly(15, 0));

        db.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-TEST-001",
            CourtId = courtId,
            UserId = clientId,
            StartTime = startUtc,
            EndTime = endUtc,
            Status = BookingStatus.Confirmed,
            TotalPrice = 200m
        });
        await db.SaveChangesAsync();

        var result = await service.GetCourtAvailabilityAsync(courtId, new CourtAvailabilityRequest
        {
            Date = futureDate,
            DurationMinutes = 60
        });

        Assert.True(result.IsSuccess);
        
        // Slot at 14:00 should be Booked
        var bookedSlot = result.Value.Slots.First(s => s.StartTime == new TimeOnly(14, 0));
        Assert.Equal(SlotAvailabilityStatus.Booked, bookedSlot.Status);

        // Adjacent slot at 15:00 should be Available
        var availableSlot = result.Value.Slots.First(s => s.StartTime == new TimeOnly(15, 0));
        Assert.Equal(SlotAvailabilityStatus.Available, availableSlot.Status);
    }
}

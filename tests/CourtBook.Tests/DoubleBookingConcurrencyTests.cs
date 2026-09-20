using CourtBook.Application.DTOs;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Services;
using Xunit;

namespace CourtBook.Tests;

public class DoubleBookingConcurrencyTests
{
    [Fact]
    public async Task CreateBooking_PreventsDoubleBooking_WhenSameSlotBooked()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, courtId, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var bookingService = new BookingService(db);

        var slotStart = DateTime.UtcNow.Date.AddDays(3).AddHours(16);
        var slotEnd = slotStart.AddHours(1);

        var request = new CreateBookingRequest
        {
            CourtId = courtId,
            StartTime = slotStart,
            EndTime = slotEnd
        };

        // First booking succeeds
        var firstBooking = await bookingService.CreateAsync(clientId, request);
        Assert.NotNull(firstBooking);
        Assert.Equal(courtId, firstBooking.CourtId);
        Assert.StartsWith("PS-", firstBooking.BookingReference);

        // Second booking for identical slot must be rejected with InvalidOperationException
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            bookingService.CreateAsync(clientId, request));

        Assert.Contains("not available", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateBooking_PreventsPartialOverlappingBooking()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, courtId, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var bookingService = new BookingService(db);

        var slotStart = DateTime.UtcNow.Date.AddDays(4).AddHours(18); // 18:00 - 20:00 (2 hours)
        var slotEnd = slotStart.AddHours(2);

        await bookingService.CreateAsync(clientId, new CreateBookingRequest
        {
            CourtId = courtId,
            StartTime = slotStart,
            EndTime = slotEnd
        });

        // Attempt overlapping booking: 19:00 - 20:00 (inside the 18:00-20:00 range)
        var overlapRequest = new CreateBookingRequest
        {
            CourtId = courtId,
            StartTime = slotStart.AddHours(1),
            EndTime = slotEnd
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            bookingService.CreateAsync(clientId, overlapRequest));

        Assert.Contains("not available", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateBooking_RejectsBookingInThePast()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, courtId, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var bookingService = new BookingService(db);

        var pastStart = DateTime.UtcNow.AddHours(-2);
        var pastEnd = pastStart.AddHours(1);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            bookingService.CreateAsync(clientId, new CreateBookingRequest
            {
                CourtId = courtId,
                StartTime = pastStart,
                EndTime = pastEnd
            }));

        Assert.Contains("past", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}

using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Services;
using Xunit;

namespace CourtBook.Tests;

public class GameBookingIntegrityTests
{
    [Fact]
    public async Task CreateBooking_Rejects_WhenOverlappingGameExistsOnCourt()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var bookingService = new BookingService(db);
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var gameStart = new TimeOnly(18, 0);
        var gameEnd = new TimeOnly(19, 0);

        // Seed an active community game on this court
        var game = new Game
        {
            Id = Guid.NewGuid(),
            VenueId = venueId,
            CourtId = courtId,
            CreatorId = clientId,
            SportType = SportType.Football,
            Title = "Friday Evening Match",
            Date = futureDate,
            StartTime = gameStart,
            EndTime = gameEnd,
            MaxPlayers = 10,
            PricePerPlayer = 25m,
            SkillLevel = SkillLevel.Intermediate,
            Status = GameStatus.Open
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();

        // Attempt to book the court at the exact same time
        var slotStartUtc = TimeZoneHelper.CreateUtcFromEgyptDateAndTime(futureDate, gameStart);
        var slotEndUtc = TimeZoneHelper.CreateUtcFromEgyptDateAndTime(futureDate, gameEnd);

        var request = new CreateBookingRequest
        {
            CourtId = courtId,
            StartTime = slotStartUtc,
            EndTime = slotEndUtc
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => bookingService.CreateAsync(clientId, request));

        Assert.Contains("community match", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateGame_Rejects_WhenOverlappingBookingExistsOnCourt()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var gameService = new GameService(db);
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(4));
        var bookingStart = new TimeOnly(16, 0);
        var bookingEnd = new TimeOnly(17, 0);

        // Seed an existing confirmed booking on this court
        var startUtc = TimeZoneHelper.CreateUtcFromEgyptDateAndTime(futureDate, bookingStart);
        var endUtc = TimeZoneHelper.CreateUtcFromEgyptDateAndTime(futureDate, bookingEnd);

        db.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-TEST-INTEG-01",
            CourtId = courtId,
            UserId = clientId,
            StartTime = startUtc,
            EndTime = endUtc,
            Status = BookingStatus.Confirmed,
            TotalPrice = 200m
        });
        await db.SaveChangesAsync();

        // Attempt to create a community game on the same court at overlapping time
        var gameRequest = new CreateGameRequest
        {
            VenueId = venueId,
            CourtId = courtId,
            SportType = "Football",
            Title = "Afternoon 5v5",
            Date = futureDate,
            StartTime = "16:00",
            EndTime = "17:00",
            MaxPlayers = 10,
            PricePerPlayer = 20m,
            SkillLevel = "AllLevels"
        };

        var result = await gameService.CreateGameAsync(clientId, gameRequest);

        Assert.False(result.IsSuccess);
        Assert.Contains("already booked", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateGame_Rejects_WhenAnotherGameAlreadyExistsAtSameTime()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var gameService = new GameService(db);
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));

        // Create first game
        var firstGameResult = await gameService.CreateGameAsync(clientId, new CreateGameRequest
        {
            VenueId = venueId,
            CourtId = courtId,
            SportType = "Football",
            Title = "Game 1",
            Date = futureDate,
            StartTime = "15:00",
            EndTime = "16:00",
            MaxPlayers = 10,
            PricePerPlayer = 20m,
            SkillLevel = "Intermediate"
        });
        Assert.True(firstGameResult.IsSuccess);

        // Attempt to create second overlapping game
        var secondGameResult = await gameService.CreateGameAsync(clientId, new CreateGameRequest
        {
            VenueId = venueId,
            CourtId = courtId,
            SportType = "Football",
            Title = "Game 2 Conflict",
            Date = futureDate,
            StartTime = "15:30",
            EndTime = "16:30",
            MaxPlayers = 10,
            PricePerPlayer = 20m,
            SkillLevel = "Intermediate"
        });

        Assert.False(secondGameResult.IsSuccess);
        Assert.Contains("already scheduled", secondGameResult.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AvailabilityService_MarksSlotAsBooked_WhenGameExists()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var availabilityService = new AvailabilityService(db);
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var gameStart = new TimeOnly(19, 0);
        var gameEnd = new TimeOnly(20, 0);

        db.Games.Add(new Game
        {
            Id = Guid.NewGuid(),
            VenueId = venueId,
            CourtId = courtId,
            CreatorId = clientId,
            SportType = SportType.Football,
            Title = "Night Game",
            Date = futureDate,
            StartTime = gameStart,
            EndTime = gameEnd,
            MaxPlayers = 10,
            PricePerPlayer = 25m,
            SkillLevel = SkillLevel.AllLevels,
            Status = GameStatus.Open
        });
        await db.SaveChangesAsync();

        var availability = await availabilityService.GetCourtAvailabilityAsync(courtId, new CourtAvailabilityRequest
        {
            Date = futureDate,
            DurationMinutes = 60
        });

        Assert.True(availability.IsSuccess);
        var gameSlot = availability.Value.Slots.First(s => s.StartTime == gameStart);
        Assert.Equal(SlotAvailabilityStatus.Booked, gameSlot.Status);

        var freeSlot = availability.Value.Slots.First(s => s.StartTime == new TimeOnly(20, 0));
        Assert.Equal(SlotAvailabilityStatus.Available, freeSlot.Status);
    }

    [Fact]
    public async Task JoinGame_Rejects_WhenGameIsFull()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var gameService = new GameService(db);

        var player2 = new User { Id = Guid.NewGuid(), Name = "Player 2", Email = "p2@test.com", PasswordHash = "h", Role = Role.Client, DateOfBirth = new DateOnly(1995, 1, 1) };
        var player3 = new User { Id = Guid.NewGuid(), Name = "Player 3", Email = "p3@test.com", PasswordHash = "h", Role = Role.Client, DateOfBirth = new DateOnly(1995, 1, 1) };
        db.Users.AddRange(player2, player3);

        var game = new Game
        {
            Id = Guid.NewGuid(),
            VenueId = venueId,
            CourtId = courtId,
            CreatorId = clientId,
            SportType = SportType.Padel,
            Title = "2-Player Padel Match",
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
            StartTime = new TimeOnly(18, 0),
            EndTime = new TimeOnly(19, 0),
            MaxPlayers = 2,
            PricePerPlayer = 50m,
            SkillLevel = SkillLevel.Intermediate,
            Status = GameStatus.Open,
            Participants = new List<GameParticipant>
            {
                new GameParticipant { Id = Guid.NewGuid(), UserId = clientId, JoinedAt = DateTime.UtcNow }
            }
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();

        // Player 2 joins -> Game fills up (2/2)
        var joinResult1 = await gameService.JoinGameAsync(player2.Id, game.Id);
        Assert.True(joinResult1.IsSuccess);

        var updatedGame = await db.Games.FindAsync(game.Id);
        Assert.Equal(GameStatus.Full, updatedGame!.Status);

        // Player 3 attempts to join full game -> Rejected
        var joinResult2 = await gameService.JoinGameAsync(player3.Id, game.Id);
        Assert.False(joinResult2.IsSuccess);
        Assert.Contains("full", joinResult2.Error.Message, StringComparison.OrdinalIgnoreCase);
    }
}

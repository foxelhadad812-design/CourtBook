using CourtBook.Application.DTOs;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Services;
using Xunit;

namespace CourtBook.Tests;

public class GameCapacityAndMatchingTests
{
    [Fact]
    public async Task Game_EnforcesMaxPlayerCapacity_AndPreventsOverJoining()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        // Add real users for players 2 and 3
        var player2 = new User { Id = Guid.NewGuid(), Name = "Player 2", Email = "p2@test.com", PasswordHash = "hash" };
        var player3 = new User { Id = Guid.NewGuid(), Name = "Player 3", Email = "p3@test.com", PasswordHash = "hash" };
        db.Users.AddRange(player2, player3);
        await db.SaveChangesAsync();

        var gameService = new GameService(db);

        // Create game with MaxPlayers = 2 (creator + 1 more player)
        var createResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Padel Doubles Match",
            SportType = "Padel",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
            StartTime = "18:00",
            EndTime = "19:30",
            MaxPlayers = 2,
            MinPlayers = 2
        });

        Assert.True(createResult.IsSuccess);
        var gameId = createResult.Value.Id;
        Assert.Equal(1, createResult.Value.CurrentPlayersCount); // Creator is player #1

        // Player 2 joins -> should succeed and mark game Full
        var join2Result = await gameService.JoinGameAsync(player2.Id, gameId);
        Assert.True(join2Result.IsSuccess);

        var gameAfterJoin = await gameService.GetByIdAsync(gameId);
        Assert.Equal("Full", gameAfterJoin.Value.Status);
        Assert.Equal(2, gameAfterJoin.Value.CurrentPlayersCount);

        // Player 3 attempts to join -> must be rejected because game is full
        var join3Result = await gameService.JoinGameAsync(player3.Id, gameId);
        Assert.True(join3Result.IsFailure);
        Assert.Contains("full", join3Result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Game_PreventsDuplicateParticipation()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var gameService = new GameService(db);

        var createResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Football 5v5",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            StartTime = "20:00",
            EndTime = "21:00",
            MaxPlayers = 10
        });

        var gameId = createResult.Value.Id;

        // Creator is already participant; attempting to join again must return Conflict
        var joinAgain = await gameService.JoinGameAsync(creatorId, gameId);
        Assert.True(joinAgain.IsFailure);
        Assert.Equal("CONFLICT", joinAgain.Error.Code);
    }
}

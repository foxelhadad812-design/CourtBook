using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CourtBook.Tests;

public class AgeAwareGamesAndDobTests
{
    private class FakeTokenService : ITokenService
    {
        public string GenerateToken(User user) => "fake-jwt-token";
    }

    [Fact]
    public async Task Register_Fails_WhenDateOfBirthIsInTheFuture()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var authService = new AuthService(db, new FakeTokenService());

        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var request = new RegisterRequest
        {
            Name = "Future Player",
            Email = "future@test.com",
            Password = "Password123!",
            Role = "Client",
            DateOfBirth = tomorrow,
            AcceptTerms = true
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => authService.RegisterAsync(request));
        Assert.Contains("cannot be in the future", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_Fails_WhenAgeIsLessThan6Years()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var authService = new AuthService(db, new FakeTokenService());

        var fourYearsAgo = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-4));
        var request = new RegisterRequest
        {
            Name = "Too Young Player",
            Email = "tooyoung@test.com",
            Password = "Password123!",
            Role = "Client",
            DateOfBirth = fourYearsAgo,
            AcceptTerms = true
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => authService.RegisterAsync(request));
        Assert.Contains("6 years old", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_Succeeds_WhenPlayerAgeIsValid()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var authService = new AuthService(db, new FakeTokenService());

        var twentyYearsAgo = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20));
        var request = new RegisterRequest
        {
            Name = "Valid Player",
            Email = "valid@test.com",
            Password = "Password123!",
            Role = "Client",
            DateOfBirth = twentyYearsAgo,
            AcceptTerms = true
        };

        var response = await authService.RegisterAsync(request);
        Assert.NotNull(response);
        Assert.Equal("Valid Player", response.Name);

        var created = await db.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
        Assert.NotNull(created);
        Assert.Equal(twentyYearsAgo, created.DateOfBirth);
    }

    [Fact]
    public async Task JoinGame_Succeeds_WhenPlayerAgeMeetsEligibility()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        // 10-year-old player
        var kidPlayer = new User
        {
            Id = Guid.NewGuid(),
            Name = "Kid Player",
            Email = "kid@test.com",
            PasswordHash = "hash",
            Role = Role.Client,
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-10))
        };
        db.Users.Add(kidPlayer);
        await db.SaveChangesAsync();

        var gameService = new GameService(db);

        // Create Kids (6–12) game
        var createResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Junior Football Cup",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
            StartTime = "16:00",
            EndTime = "17:00",
            AgeGroup = AgeGroup.Kids.ToString(),
            MaxPlayers = 10
        });

        Assert.True(createResult.IsSuccess);
        var gameId = createResult.Value.Id;

        // 10-year-old joins -> should succeed
        var joinResult = await gameService.JoinGameAsync(kidPlayer.Id, gameId);
        Assert.True(joinResult.IsSuccess);
    }

    [Fact]
    public async Task JoinGame_FailsWithExactRequiredMessage_WhenPlayerAgeDoesNotMeetEligibility()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        // 16-year-old teen player
        var teenPlayer = new User
        {
            Id = Guid.NewGuid(),
            Name = "Teen Player",
            Email = "teen@test.com",
            PasswordHash = "hash",
            Role = Role.Client,
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-16))
        };
        db.Users.Add(teenPlayer);
        await db.SaveChangesAsync();

        var gameService = new GameService(db);

        // Create Kids (6–12) game
        var createResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Kids Academy Fun Match",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
            StartTime = "15:00",
            EndTime = "16:00",
            AgeGroup = AgeGroup.Kids.ToString(),
            MaxPlayers = 10
        });

        Assert.True(createResult.IsSuccess);
        var gameId = createResult.Value.Id;

        // 16-year-old tries to join Kids game -> must be rejected with exact required message
        var joinResult = await gameService.JoinGameAsync(teenPlayer.Id, gameId);
        Assert.True(joinResult.IsFailure);
        Assert.Equal("You can't join this game because your age does not meet the game's eligibility requirements.", joinResult.Error.Message);
    }

    [Fact]
    public async Task JoinGame_FailsWithExactRequiredMessage_WhenUnderagePlayerJoinsAdultsGame()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        // 15-year-old player
        var underagePlayer = new User
        {
            Id = Guid.NewGuid(),
            Name = "Underage Player",
            Email = "underage@test.com",
            PasswordHash = "hash",
            Role = Role.Client,
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-15))
        };
        db.Users.Add(underagePlayer);
        await db.SaveChangesAsync();

        var gameService = new GameService(db);

        // Create Adults (18+) game
        var createResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Friday Evening Adults Padel",
            SportType = "Padel",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            StartTime = "20:00",
            EndTime = "21:30",
            AgeGroup = AgeGroup.Adults.ToString(),
            MaxPlayers = 4
        });

        Assert.True(createResult.IsSuccess);
        var gameId = createResult.Value.Id;

        // 15-year-old tries to join Adults game -> must be rejected with exact required message
        var joinResult = await gameService.JoinGameAsync(underagePlayer.Id, gameId);
        Assert.True(joinResult.IsFailure);
        Assert.Equal("You can't join this game because your age does not meet the game's eligibility requirements.", joinResult.Error.Message);
    }

    [Fact]
    public async Task JoinGame_Succeeds_WhenAdultJoinsAdultsGame()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        // 25-year-old adult player
        var adultPlayer = new User
        {
            Id = Guid.NewGuid(),
            Name = "Adult Player",
            Email = "adult@test.com",
            PasswordHash = "hash",
            Role = Role.Client,
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-25))
        };
        db.Users.Add(adultPlayer);
        await db.SaveChangesAsync();

        var gameService = new GameService(db);

        var createResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Adults Competitive Basketball",
            SportType = "Basketball",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            StartTime = "19:00",
            EndTime = "20:30",
            AgeGroup = AgeGroup.Adults.ToString(),
            MaxPlayers = 10
        });

        var gameId = createResult.Value.Id;
        var joinResult = await gameService.JoinGameAsync(adultPlayer.Id, gameId);
        Assert.True(joinResult.IsSuccess);
    }

    [Fact]
    public async Task JoinGame_SucceedsForAllAges_RegardlessOfPlayerAge()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var player12 = new User
        {
            Id = Guid.NewGuid(),
            Name = "Player 12",
            Email = "p12@test.com",
            PasswordHash = "hash",
            Role = Role.Client,
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-12))
        };
        var player35 = new User
        {
            Id = Guid.NewGuid(),
            Name = "Player 35",
            Email = "p35@test.com",
            PasswordHash = "hash",
            Role = Role.Client,
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-35))
        };
        db.Users.AddRange(player12, player35);
        await db.SaveChangesAsync();

        var gameService = new GameService(db);

        var createResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "All Ages Friendly Tennis",
            SportType = "Tennis",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(4)),
            StartTime = "17:00",
            EndTime = "18:00",
            AgeGroup = AgeGroup.AllAges.ToString(),
            MaxPlayers = 4
        });

        var gameId = createResult.Value.Id;

        var join1 = await gameService.JoinGameAsync(player12.Id, gameId);
        Assert.True(join1.IsSuccess);

        var join2 = await gameService.JoinGameAsync(player35.Id, gameId);
        Assert.True(join2.IsSuccess);
    }

    [Fact]
    public async Task JoinGame_RejectsPlayerWithoutDateOfBirth_ForAgeRestrictedGame()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var playerNoDob = new User
        {
            Id = Guid.NewGuid(),
            Name = "No DOB Player",
            Email = "nodob@test.com",
            PasswordHash = "hash",
            Role = Role.Client,
            DateOfBirth = null
        };
        db.Users.Add(playerNoDob);
        await db.SaveChangesAsync();

        var gameService = new GameService(db);

        var createResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Teens Only Match",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
            StartTime = "17:00",
            EndTime = "18:00",
            AgeGroup = AgeGroup.Teens.ToString(),
            MaxPlayers = 10
        });

        var gameId = createResult.Value.Id;

        var joinResult = await gameService.JoinGameAsync(playerNoDob.Id, gameId);
        Assert.True(joinResult.IsFailure);
        Assert.Contains("date of birth", joinResult.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GamePrivacy_NeverExposesExactDobOrAge_InPublicDto()
    {
        // Assert that GameResponse and GameParticipantDto do not contain DateOfBirth or numeric Age properties
        var gameResponseType = typeof(GameResponse);
        Assert.Null(gameResponseType.GetProperty("DateOfBirth"));
        Assert.Null(gameResponseType.GetProperty("PlayerAge"));
        Assert.Null(gameResponseType.GetProperty("UserAge"));

        var participantType = typeof(GameParticipantDto);
        Assert.Null(participantType.GetProperty("DateOfBirth"));
        Assert.Null(participantType.GetProperty("Age"));
    }
}

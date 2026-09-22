using CourtBook.Application.DTOs;
using CourtBook.Application.Validators;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Services;
using Xunit;

namespace CourtBook.Tests;

public class Phase92MatchmakingTests
{
    [Fact]
    public async Task PlayerSportSkill_Upsert_CreatesAndUpdatesSkillRatingWithAccurateDefaults()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, _, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var service = new MatchmakingService(db);

        // 1. Create Football skill (Beginner default -> 1000 score)
        var createResult = await service.UpsertPlayerSkillAsync(clientId, new UpsertPlayerSportSkillRequest
        {
            SportType = "Football",
            SkillLevel = "Beginner"
        });

        Assert.True(createResult.IsSuccess);
        Assert.Equal("Football", createResult.Value.SportType);
        Assert.Equal("Beginner", createResult.Value.SkillLevel);
        Assert.Equal(1000, createResult.Value.SkillScore);

        // 2. Create Padel skill with custom score
        var padelResult = await service.UpsertPlayerSkillAsync(clientId, new UpsertPlayerSportSkillRequest
        {
            SportType = "Padel",
            SkillLevel = "Intermediate",
            SkillScore = 1650
        });

        Assert.True(padelResult.IsSuccess);
        Assert.Equal("Padel", padelResult.Value.SportType);
        Assert.Equal(1650, padelResult.Value.SkillScore);

        // 3. Update Football skill to Advanced
        var updateResult = await service.UpsertPlayerSkillAsync(clientId, new UpsertPlayerSportSkillRequest
        {
            SportType = "Football",
            SkillLevel = "Advanced"
        });

        Assert.True(updateResult.IsSuccess);
        Assert.Equal(2000, updateResult.Value.SkillScore);

        // Verify list retrieves both skills
        var listResult = await service.GetPlayerSkillsAsync(clientId);
        Assert.True(listResult.IsSuccess);
        Assert.Equal(2, listResult.Value.Count);
    }

    [Fact]
    public async Task PlayerPreference_Updates_AndRetrievesPreferencesAccurately()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, _, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var service = new MatchmakingService(db);

        var updateReq = new UpdatePlayerPreferenceRequest
        {
            PreferredSports = ["Football", "Padel"],
            PreferredCities = ["Cairo", "Giza"],
            PreferredDays = ["Friday", "Saturday"],
            PreferredTimeOfDay = ["Evening"],
            PreferredGameType = "Competitive",
            MaxDistanceKm = 20,
            PreferredSkillLevel = "Intermediate"
        };

        var updateResult = await service.UpdatePlayerPreferenceAsync(clientId, updateReq);
        Assert.True(updateResult.IsSuccess);
        Assert.Equal(2, updateResult.Value.PreferredSports.Count);
        Assert.Equal(20, updateResult.Value.MaxDistanceKm);
        Assert.Equal("Competitive", updateResult.Value.PreferredGameType);

        var getResult = await service.GetPlayerPreferenceAsync(clientId);
        Assert.True(getResult.IsSuccess);
        Assert.Contains("Football", getResult.Value.PreferredSports);
        Assert.Contains("Cairo", getResult.Value.PreferredCities);
        Assert.Equal("Intermediate", getResult.Value.PreferredSkillLevel);
    }

    [Fact]
    public async Task Matchmaking_CalculatesDeterministicRecommendationScore_WithBreakdown()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        // Setup candidate game creator
        var creator = new User { Id = Guid.NewGuid(), Name = "Organizer", Email = "org@test.com", PasswordHash = "hash" };
        db.Users.Add(creator);
        await db.SaveChangesAsync();

        var gameDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
        var gameDayName = gameDate.DayOfWeek.ToString();

        // Configure client preferences for perfect match: Football, Cairo, gameDayName, Evening
        var prefService = new MatchmakingService(db);
        await prefService.UpdatePlayerPreferenceAsync(clientId, new UpdatePlayerPreferenceRequest
        {
            PreferredSports = ["Football"],
            PreferredCities = ["Cairo"],
            PreferredDays = [gameDayName],
            PreferredTimeOfDay = ["Evening"],
            PreferredSkillLevel = "Intermediate"
        });

        // Add client's Football skill as Intermediate
        await prefService.UpsertPlayerSkillAsync(clientId, new UpsertPlayerSportSkillRequest
        {
            SportType = "Football",
            SkillLevel = "Intermediate"
        });

        // Create matching game
        var gameService = new GameService(db);
        var createResult = await gameService.CreateGameAsync(creator.Id, new CreateGameRequest
        {
            Title = "Cairo Evening Football Match",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = gameDate,
            StartTime = "18:00", // Evening: 17:00 - 22:00
            EndTime = "19:30",
            SkillLevel = "Intermediate",
            MaxPlayers = 10,
            MinPlayers = 2
        });
        Assert.True(createResult.IsSuccess);

        // Fetch matchmaking recommendations
        var recsResult = await prefService.GetRecommendationsAsync(clientId);
        Assert.True(recsResult.IsSuccess);
        Assert.NotEmpty(recsResult.Value);

        var top = recsResult.Value.First();
        // Exact sport (40) + exact skill (25) + day(10)+time(10) (20) + city (15) = 100
        Assert.Equal(100.0, top.MatchScore);
        Assert.Equal(40.0, top.Breakdown.SportScore);
        Assert.Equal(25.0, top.Breakdown.SkillScore);
        Assert.Equal(20.0, top.Breakdown.TimeScore);
        Assert.Equal(15.0, top.Breakdown.LocationScore);
        Assert.Contains("Preferred sport (Football)", top.Explanation);
    }

    [Fact]
    public async Task Matchmaking_RanksCandidateGames_ByHighestCompatibilityScore()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, clientId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var creator = new User { Id = Guid.NewGuid(), Name = "Organizer", Email = "org2@test.com", PasswordHash = "hash" };
        db.Users.Add(creator);
        await db.SaveChangesAsync();

        var mmService = new MatchmakingService(db);
        await mmService.UpdatePlayerPreferenceAsync(clientId, new UpdatePlayerPreferenceRequest
        {
            PreferredSports = ["Football"],
            PreferredCities = ["Cairo"]
        });

        var gameDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(4));

        // Create a secondary court for Padel
        var padelCourt = new Court
        {
            Id = Guid.NewGuid(),
            VenueId = venueId,
            Name = "Padel Court 1",
            SportType = SportType.Padel,
            PricePerHour = 300,
            IsActive = true
        };
        for (int i = 0; i < 7; i++)
        {
            padelCourt.Schedules.Add(new CourtSchedule
            {
                Id = Guid.NewGuid(),
                CourtId = padelCourt.Id,
                DayOfWeek = (DayOfWeek)i,
                OpenTime = new TimeOnly(8, 0),
                CloseTime = new TimeOnly(23, 0)
            });
        }
        db.Courts.Add(padelCourt);
        await db.SaveChangesAsync();

        var gameService = new GameService(db);
        // Game 1: Football (High score)
        await gameService.CreateGameAsync(creator.Id, new CreateGameRequest
        {
            Title = "Match A - Football",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = gameDate,
            StartTime = "10:00",
            EndTime = "11:30",
            MaxPlayers = 10
        });

        // Game 2: Padel (Lower score for football preferrer)
        await gameService.CreateGameAsync(creator.Id, new CreateGameRequest
        {
            Title = "Match B - Padel",
            SportType = "Padel",
            VenueId = venueId,
            CourtId = padelCourt.Id,
            Date = gameDate,
            StartTime = "12:00",
            EndTime = "13:30",
            MaxPlayers = 4
        });

        var recs = await mmService.GetRecommendationsAsync(clientId);
        Assert.True(recs.IsSuccess);
        Assert.Equal(2, recs.Value.Count);
        Assert.True(recs.Value[0].MatchScore > recs.Value[1].MatchScore);
        Assert.Equal("Football", recs.Value[0].Game.SportType);
    }

    [Fact]
    public async Task Game_Private_RequiresValidAccessCode_ToJoin()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var player = new User { Id = Guid.NewGuid(), Name = "Player X", Email = "px@test.com", PasswordHash = "hash" };
        db.Users.Add(player);
        await db.SaveChangesAsync();

        var gameService = new GameService(db);
        var createResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Private Friends Match",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            StartTime = "19:00",
            EndTime = "20:30",
            IsPrivate = true,
            AccessCode = "VIP777",
            MaxPlayers = 10
        });

        Assert.True(createResult.IsSuccess);
        var gameId = createResult.Value.Id;
        Assert.True(createResult.Value.IsPrivate);
        Assert.Equal("VIP777", createResult.Value.AccessCode);

        // Attempt 1: Join with no access code -> Forbidden
        var noCodeJoin = await gameService.JoinGameAsync(player.Id, gameId, null);
        Assert.True(noCodeJoin.IsFailure);
        Assert.Equal("FORBIDDEN", noCodeJoin.Error.Code);

        // Attempt 2: Join with wrong code -> Forbidden
        var wrongCodeJoin = await gameService.JoinGameAsync(player.Id, gameId, "WRONG1");
        Assert.True(wrongCodeJoin.IsFailure);
        Assert.Equal("FORBIDDEN", wrongCodeJoin.Error.Code);

        // Attempt 3: Join with correct code -> Success
        var correctJoin = await gameService.JoinGameAsync(player.Id, gameId, "vip777");
        Assert.True(correctJoin.IsSuccess);

        var game = await gameService.GetByIdAsync(gameId);
        Assert.Equal(2, game.Value.CurrentPlayersCount);
    }

    [Fact]
    public async Task Game_Private_ExcludedFromDefaultSearch()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var gameService = new GameService(db);

        await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Secret Game",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            StartTime = "18:00",
            EndTime = "19:30",
            IsPrivate = true,
            AccessCode = "SEC001",
            MaxPlayers = 8
        });

        // Default search without IncludePrivate
        var defaultSearch = await gameService.SearchGamesAsync(new GameSearchRequest());
        Assert.Empty(defaultSearch.Items);

        // Search with IncludePrivate = true
        var privateSearch = await gameService.SearchGamesAsync(new GameSearchRequest { IncludePrivate = true });
        Assert.Single(privateSearch.Items);
    }

    [Fact]
    public async Task Game_DuplicateJoin_ReturnsConflict()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);
        var gameService = new GameService(db);

        var createResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Duplicate Join Test",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
            StartTime = "18:00",
            EndTime = "19:30",
            MaxPlayers = 6
        });

        var gameId = createResult.Value.Id;

        // Creator already joined in CreateGameAsync
        var duplicateJoin = await gameService.JoinGameAsync(creatorId, gameId);
        Assert.True(duplicateJoin.IsFailure);
        Assert.Equal("CONFLICT", duplicateJoin.Error.Code);
    }

    [Fact]
    public async Task GameLobby_SetReady_UpdatesPlayerStateAndAllPlayersReadyFlag()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var player2 = new User { Id = Guid.NewGuid(), Name = "Player 2", Email = "p2@lobby.com", PasswordHash = "hash" };
        db.Users.Add(player2);
        await db.SaveChangesAsync();

        var gameService = new GameService(db);
        var createResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Ready State Test",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            StartTime = "18:00",
            EndTime = "19:30",
            MaxPlayers = 2,
            MinPlayers = 2
        });

        var gameId = createResult.Value.Id;

        // Player 2 joins
        await gameService.JoinGameAsync(player2.Id, gameId);

        // Creator is ready, Player 2 is not yet ready -> AllPlayersReady is false
        var lobby1 = await gameService.GetGameLobbyAsync(creatorId, gameId);
        Assert.True(lobby1.IsSuccess);
        Assert.False(lobby1.Value.AllPlayersReady);

        // Player 2 sets ready
        var readyResult = await gameService.SetPlayerReadyAsync(player2.Id, gameId, true);
        Assert.True(readyResult.IsSuccess);

        var lobby2 = await gameService.GetGameLobbyAsync(player2.Id, gameId);
        Assert.True(lobby2.Value.AllPlayersReady);
    }

    [Fact]
    public async Task GameLobby_BalanceTeams_UsesSnakeDraft_AndMinimizesSkillGap()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        // Create 3 additional players with varying skill scores
        var p2 = new User { Id = Guid.NewGuid(), Name = "Pro Player", Email = "pro@test.com", PasswordHash = "hash" };
        var p3 = new User { Id = Guid.NewGuid(), Name = "Mid Player", Email = "mid@test.com", PasswordHash = "hash" };
        var p4 = new User { Id = Guid.NewGuid(), Name = "Rookie Player", Email = "rookie@test.com", PasswordHash = "hash" };
        db.Users.AddRange(p2, p3, p4);

        // Creator rating = 1500
        db.PlayerSportSkills.Add(new PlayerSportSkill { Id = Guid.NewGuid(), UserId = creatorId, SportType = SportType.Football, SkillScore = 1500, SkillLevel = SkillLevel.Intermediate });
        // p2 rating = 2200
        db.PlayerSportSkills.Add(new PlayerSportSkill { Id = Guid.NewGuid(), UserId = p2.Id, SportType = SportType.Football, SkillScore = 2200, SkillLevel = SkillLevel.Advanced });
        // p3 rating = 1800
        db.PlayerSportSkills.Add(new PlayerSportSkill { Id = Guid.NewGuid(), UserId = p3.Id, SportType = SportType.Football, SkillScore = 1800, SkillLevel = SkillLevel.Intermediate });
        // p4 rating = 1100
        db.PlayerSportSkills.Add(new PlayerSportSkill { Id = Guid.NewGuid(), UserId = p4.Id, SportType = SportType.Football, SkillScore = 1100, SkillLevel = SkillLevel.Beginner });
        await db.SaveChangesAsync();

        var gameService = new GameService(db);
        var createResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Snake Draft 4v4",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(4)),
            StartTime = "18:00",
            EndTime = "19:30",
            MaxPlayers = 4
        });

        var gameId = createResult.Value.Id;
        await gameService.JoinGameAsync(p2.Id, gameId);
        await gameService.JoinGameAsync(p3.Id, gameId);
        await gameService.JoinGameAsync(p4.Id, gameId);

        // Non-organizer attempts to balance teams -> Forbidden
        var forbiddenResult = await gameService.BalanceTeamsAsync(p2.Id, gameId);
        Assert.True(forbiddenResult.IsFailure);
        Assert.Equal("FORBIDDEN", forbiddenResult.Error.Code);

        // Organizer balances teams
        var balanceResult = await gameService.BalanceTeamsAsync(creatorId, gameId);
        Assert.True(balanceResult.IsSuccess);

        var balance = balanceResult.Value;
        Assert.Equal(2, balance.TeamAPlayers.Count);
        Assert.Equal(2, balance.TeamBPlayers.Count);

        // Players ordered by score: p2(2200), p3(1800), creator(1500), p4(1100)
        // Snake draft distribution:
        // Rank 0 (2200) -> Team A
        // Rank 1 (1800) -> Team B
        // Rank 2 (1500) -> Team B
        // Rank 3 (1100) -> Team A
        // Team A = 2200 + 1100 = 3300
        // Team B = 1800 + 1500 = 3300
        // Score Difference = 0!
        Assert.Equal(3300, balance.TeamATotalScore);
        Assert.Equal(3300, balance.TeamBTotalScore);
        Assert.Equal(0, balance.ScoreDifference);
    }

    [Fact]
    public async Task GameLobby_LeaveGame_RestoresStatusToOpen_WhenWasFull()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var player2 = new User { Id = Guid.NewGuid(), Name = "Player 2", Email = "p2@full.com", PasswordHash = "hash" };
        db.Users.Add(player2);
        await db.SaveChangesAsync();

        var gameService = new GameService(db);
        var createResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Status Transition Test",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            StartTime = "18:00",
            EndTime = "19:30",
            MaxPlayers = 2
        });

        var gameId = createResult.Value.Id;
        await gameService.JoinGameAsync(player2.Id, gameId);

        var fullGame = await gameService.GetByIdAsync(gameId);
        Assert.Equal("Full", fullGame.Value.Status);

        // Player 2 leaves
        var leaveResult = await gameService.LeaveGameAsync(player2.Id, gameId);
        Assert.True(leaveResult.IsSuccess);

        var openGame = await gameService.GetByIdAsync(gameId);
        Assert.Equal("Open", openGame.Value.Status);
        Assert.Equal(1, openGame.Value.CurrentPlayersCount);
    }

    [Fact]
    public async Task Game_ConcurrentJoin_ProtectsCapacityUnderRaceConditions()
    {
        var dbName = Guid.NewGuid().ToString();
        var seedDb = TestDbContextFactory.Create(dbName);
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(seedDb);

        var racer1 = new User { Id = Guid.NewGuid(), Name = "Racer 1", Email = "r1@race.com", PasswordHash = "hash" };
        var racer2 = new User { Id = Guid.NewGuid(), Name = "Racer 2", Email = "r2@race.com", PasswordHash = "hash" };
        seedDb.Users.AddRange(racer1, racer2);
        await seedDb.SaveChangesAsync();

        var gameService = new GameService(seedDb);
        var createResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Capacity Race Match",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
            StartTime = "18:00",
            EndTime = "19:30",
            MaxPlayers = 2 // Exactly 1 spot remaining after creator
        });
        Assert.True(createResult.IsSuccess);
        var gameId = createResult.Value.Id;

        // Two concurrent clients attempt to join the single available spot
        var task1 = Task.Run(async () =>
        {
            var db1 = TestDbContextFactory.Create(dbName);
            var svc1 = new GameService(db1);
            return await svc1.JoinGameAsync(racer1.Id, gameId);
        });

        var task2 = Task.Run(async () =>
        {
            var db2 = TestDbContextFactory.Create(dbName);
            var svc2 = new GameService(db2);
            return await svc2.JoinGameAsync(racer2.Id, gameId);
        });

        var results = await Task.WhenAll(task1, task2);

        // Exactly one racer must succeed, and one must be rejected
        var successCount = results.Count(r => r.IsSuccess);
        var failureCount = results.Count(r => r.IsFailure);

        Assert.Equal(1, successCount);
        Assert.Equal(1, failureCount);

        var verifyDb = TestDbContextFactory.Create(dbName);
        var verifyService = new GameService(verifyDb);
        var finalGame = await verifyService.GetByIdAsync(gameId);

        // Capacity is never exceeded
        Assert.Equal(2, finalGame.Value.CurrentPlayersCount);
        Assert.Equal("Full", finalGame.Value.Status);
    }

    [Fact]
    public async Task Matchmaking_ExcludesIneligibleAgeAndAlreadyJoinedGames()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        // Adult user (born 25 years ago)
        var adultPlayer = new User
        {
            Id = Guid.NewGuid(),
            Name = "Adult Player",
            Email = "adult@test.com",
            PasswordHash = "hash",
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-25))
        };
        db.Users.Add(adultPlayer);
        await db.SaveChangesAsync();

        var gameService = new GameService(db);
        var mmService = new MatchmakingService(db);

        // 1. Kids only match (Ages 6-12)
        await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Junior Stars Football",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
            StartTime = "16:00",
            EndTime = "17:30",
            AgeGroup = "Kids",
            MaxPlayers = 10
        });

        // 2. Open adults match
        var adultGame = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Adults Football Clash",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            StartTime = "18:00",
            EndTime = "19:30",
            AgeGroup = "Adults",
            MaxPlayers = 10
        });

        // Adult player gets recommendations
        var recs = await mmService.GetRecommendationsAsync(adultPlayer.Id);
        Assert.True(recs.IsSuccess);

        // Kids game must be excluded because adult is age-ineligible!
        Assert.Single(recs.Value);
        Assert.Equal("Adults Football Clash", recs.Value[0].Game.Title);

        // Join adult game -> next recommendations query must exclude it as well
        await gameService.JoinGameAsync(adultPlayer.Id, adultGame.Value.Id);
        var recsAfterJoin = await mmService.GetRecommendationsAsync(adultPlayer.Id);
        Assert.True(recsAfterJoin.IsSuccess);
        Assert.Empty(recsAfterJoin.Value);
    }

    [Fact]
    public async Task GameLobby_AssignTeam_RestrictedToOrganizer()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var player2 = new User { Id = Guid.NewGuid(), Name = "Player 2", Email = "p2@assign.com", PasswordHash = "hash" };
        var player3 = new User { Id = Guid.NewGuid(), Name = "Player 3", Email = "p3@assign.com", PasswordHash = "hash" };
        db.Users.AddRange(player2, player3);
        await db.SaveChangesAsync();

        var gameService = new GameService(db);
        var createResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Team Assignment Game",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
            StartTime = "18:00",
            EndTime = "19:30",
            MaxPlayers = 4
        });
        var gameId = createResult.Value.Id;

        await gameService.JoinGameAsync(player2.Id, gameId);
        await gameService.JoinGameAsync(player3.Id, gameId);

        // Non-organizer attempts to assign team -> Forbidden
        var unauthorizedAssign = await gameService.AssignTeamAsync(player2.Id, gameId, player3.Id, "TeamA");
        Assert.True(unauthorizedAssign.IsFailure);
        Assert.Equal("FORBIDDEN", unauthorizedAssign.Error.Code);

        // Organizer assigns player 2 to TeamB -> Success
        var organizerAssign = await gameService.AssignTeamAsync(creatorId, gameId, player2.Id, "TeamB");
        Assert.True(organizerAssign.IsSuccess);

        var lobby = await gameService.GetGameLobbyAsync(creatorId, gameId);
        var p2Lobby = lobby.Value.Participants.First(p => p.UserId == player2.Id);
        Assert.Equal("TeamB", p2Lobby.Team);
    }

    [Fact]
    public async Task Game_Private_DoesNotLeakAccessCodeOrRoster_ToUnauthorizedUsers()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var stranger = new User { Id = Guid.NewGuid(), Name = "Stranger", Email = "stranger@test.com", PasswordHash = "hash" };
        db.Users.Add(stranger);
        await db.SaveChangesAsync();

        var gameService = new GameService(db);
        var createResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Top Secret VIP Match",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            StartTime = "19:00",
            EndTime = "20:30",
            IsPrivate = true,
            AccessCode = "SECRET123",
            MaxPlayers = 10
        });

        Assert.True(createResult.IsSuccess);
        var gameId = createResult.Value.Id;

        // 1. Anonymous caller: AccessCode must be hidden, Participants roster empty
        var anonView = await gameService.GetByIdAsync(gameId, currentUserId: null);
        Assert.True(anonView.IsSuccess);
        Assert.Null(anonView.Value.AccessCode);
        Assert.Empty(anonView.Value.Participants);
        Assert.Equal(1, anonView.Value.CurrentPlayersCount);

        // 2. Authenticated non-participant stranger: AccessCode hidden, Participants roster empty
        var strangerView = await gameService.GetByIdAsync(gameId, currentUserId: stranger.Id);
        Assert.True(strangerView.IsSuccess);
        Assert.Null(strangerView.Value.AccessCode);
        Assert.Empty(strangerView.Value.Participants);

        // 3. Creator view: AccessCode visible, Participants roster visible
        var creatorView = await gameService.GetByIdAsync(gameId, currentUserId: creatorId);
        Assert.True(creatorView.IsSuccess);
        Assert.Equal("SECRET123", creatorView.Value.AccessCode);
        Assert.NotEmpty(creatorView.Value.Participants);

        // 4. Stranger joins with valid code -> Stranger can now view roster, but not AccessCode
        var joinResult = await gameService.JoinGameAsync(stranger.Id, gameId, "SECRET123");
        Assert.True(joinResult.IsSuccess);

        var memberView = await gameService.GetByIdAsync(gameId, currentUserId: stranger.Id);
        Assert.True(memberView.IsSuccess);
        Assert.Null(memberView.Value.AccessCode);
        Assert.Equal(2, memberView.Value.Participants.Count);
    }

    [Fact]
    public async Task GameLobby_StateGuards_RejectModificationsOnCompletedCancelledOrStartedMatches()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var player2 = new User { Id = Guid.NewGuid(), Name = "Player 2", Email = "p2@guard.com", PasswordHash = "hash" };
        db.Users.Add(player2);
        await db.SaveChangesAsync();

        var gameService = new GameService(db);
        var createResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "State Guard Game",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
            StartTime = "18:00",
            EndTime = "19:30",
            MaxPlayers = 4
        });
        var gameId = createResult.Value.Id;
        await gameService.JoinGameAsync(player2.Id, gameId);

        // Cancel the game
        var cancelResult = await gameService.CancelGameAsync(creatorId, "Client", gameId);
        Assert.True(cancelResult.IsSuccess);

        // All modifications on cancelled match must be rejected with BadRequest
        var readyResult = await gameService.SetPlayerReadyAsync(player2.Id, gameId, true);
        Assert.True(readyResult.IsFailure);
        Assert.Equal("BAD_REQUEST", readyResult.Error.Code);

        var assignResult = await gameService.AssignTeamAsync(creatorId, gameId, player2.Id, "TeamA");
        Assert.True(assignResult.IsFailure);
        Assert.Equal("BAD_REQUEST", assignResult.Error.Code);

        var balanceResult = await gameService.BalanceTeamsAsync(creatorId, gameId);
        Assert.True(balanceResult.IsFailure);
        Assert.Equal("BAD_REQUEST", balanceResult.Error.Code);

        var leaveResult = await gameService.LeaveGameAsync(player2.Id, gameId);
        Assert.True(leaveResult.IsFailure);
        Assert.Equal("BAD_REQUEST", leaveResult.Error.Code);
    }

    [Fact]
    public async Task Matchmaking_ExcludesFullMatches_FromRecommendations()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var player2 = new User { Id = Guid.NewGuid(), Name = "Player 2", Email = "p2@fullmatch.com", PasswordHash = "hash" };
        var candidatePlayer = new User { Id = Guid.NewGuid(), Name = "Candidate", Email = "cand@test.com", PasswordHash = "hash" };
        db.Users.AddRange(player2, candidatePlayer);
        await db.SaveChangesAsync();

        var gameService = new GameService(db);
        var mmService = new MatchmakingService(db);

        // Create a game with capacity 2
        var createResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Full 2-player Match",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
            StartTime = "18:00",
            EndTime = "19:30",
            MaxPlayers = 2
        });
        var gameId = createResult.Value.Id;

        // Player 2 joins -> Game is now full
        await gameService.JoinGameAsync(player2.Id, gameId);

        // Candidate requests recommendations -> Full game should not be recommended
        var recs = await mmService.GetRecommendationsAsync(candidatePlayer.Id);
        Assert.True(recs.IsSuccess);
        Assert.DoesNotContain(recs.Value, r => r.Game.Id == gameId);
    }

    [Fact]
    public void FluentValidators_EnforceCorrectness_OnRequests()
    {
        var createValidator = new CreateGameRequestValidator();
        var invalidCreate = new CreateGameRequest
        {
            Title = "A", // too short
            SportType = "Quidditch", // invalid sport
            VenueId = Guid.Empty,
            CourtId = Guid.Empty,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)), // past
            StartTime = "invalid-time",
            EndTime = "invalid-time",
            MaxPlayers = 100, // > 50
            MinPlayers = 200 // > MaxPlayers
        };
        var createValidation = createValidator.Validate(invalidCreate);
        Assert.False(createValidation.IsValid);
        Assert.Contains(createValidation.Errors, e => e.PropertyName == nameof(CreateGameRequest.Title));
        Assert.Contains(createValidation.Errors, e => e.PropertyName == nameof(CreateGameRequest.SportType));
        Assert.Contains(createValidation.Errors, e => e.PropertyName == nameof(CreateGameRequest.MaxPlayers));
        Assert.Contains(createValidation.Errors, e => e.PropertyName == nameof(CreateGameRequest.MinPlayers));

        var skillValidator = new UpsertPlayerSportSkillRequestValidator();
        var invalidSkill = new UpsertPlayerSportSkillRequest
        {
            SportType = "UnderwaterRugby",
            SkillScore = 9999
        };
        var skillValidation = skillValidator.Validate(invalidSkill);
        Assert.False(skillValidation.IsValid);
        Assert.Contains(skillValidation.Errors, e => e.PropertyName == nameof(UpsertPlayerSportSkillRequest.SportType));
        Assert.Contains(skillValidation.Errors, e => e.PropertyName == nameof(UpsertPlayerSportSkillRequest.SkillScore));

        var teamValidator = new AssignTeamRequestValidator();
        var invalidTeam = new AssignTeamRequest
        {
            ParticipantUserId = Guid.Empty,
            Team = "InvalidTeamName"
        };
        var teamValidation = teamValidator.Validate(invalidTeam);
        Assert.False(teamValidation.IsValid);
        Assert.Contains(teamValidation.Errors, e => e.PropertyName == nameof(AssignTeamRequest.ParticipantUserId));
        Assert.Contains(teamValidation.Errors, e => e.PropertyName == nameof(AssignTeamRequest.Team));
    }
}

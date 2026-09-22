using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Validators;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CourtBook.Tests;

public class Phase93CommunityTests
{
    // ==========================================
    // 1. GAME INVITATION TESTS
    // ==========================================

    [Fact]
    public async Task Invitation_Create_SucceedsAndDispatchesNotification()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var playerB = new User { Id = Guid.NewGuid(), Name = "Player B", Email = "pb@test.com", PasswordHash = "hash" };
        db.Users.Add(playerB);
        await db.SaveChangesAsync();

        var notifService = new NotificationService(db, NullLogger<NotificationService>.Instance);
        var gameService = new GameService(db);
        var invService = new InvitationService(db, gameService, notifService, NullLogger<InvitationService>.Instance);

        var gameResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Friday Football Clash",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            StartTime = "18:00",
            EndTime = "19:30",
            MaxPlayers = 10
        });
        Assert.True(gameResult.IsSuccess);
        var gameId = gameResult.Value.Id;

        // Creator invites Player B
        var invResult = await invService.CreateInvitationAsync(creatorId, gameId, new CreateInvitationRequest
        {
            InviteeId = playerB.Id,
            Message = "Hey, join our team on Friday!"
        });

        Assert.True(invResult.IsSuccess);
        Assert.Equal("Pending", invResult.Value.Status);
        Assert.Equal(playerB.Id, invResult.Value.InviteeId);
        Assert.Equal("Friday Football Clash", invResult.Value.GameTitle);
        Assert.Equal("Hey, join our team on Friday!", invResult.Value.Message);

        // Verify notification dispatched to Player B
        var unread = await notifService.GetUnreadCountAsync(playerB.Id);
        Assert.Equal(1, unread);
    }

    [Fact]
    public async Task Invitation_SelfInvite_FailsWithBadRequest()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var gameService = new GameService(db);
        var notifService = new NotificationService(db, NullLogger<NotificationService>.Instance);
        var invService = new InvitationService(db, gameService, notifService, NullLogger<InvitationService>.Instance);

        var gameResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Match A",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
            StartTime = "18:00",
            EndTime = "19:30",
            MaxPlayers = 6
        });

        var result = await invService.CreateInvitationAsync(creatorId, gameResult.Value.Id, new CreateInvitationRequest
        {
            InviteeId = creatorId
        });

        Assert.True(result.IsFailure);
        Assert.Equal("BAD_REQUEST", result.Error.Code);
    }

    [Fact]
    public async Task Invitation_UnauthorizedUser_FailsWithForbidden()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var stranger = new User { Id = Guid.NewGuid(), Name = "Stranger", Email = "stranger@test.com", PasswordHash = "hash" };
        var invitee = new User { Id = Guid.NewGuid(), Name = "Invitee", Email = "invitee@test.com", PasswordHash = "hash" };
        db.Users.AddRange(stranger, invitee);
        await db.SaveChangesAsync();

        var gameService = new GameService(db);
        var notifService = new NotificationService(db, NullLogger<NotificationService>.Instance);
        var invService = new InvitationService(db, gameService, notifService, NullLogger<InvitationService>.Instance);

        var gameResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Private Game",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
            StartTime = "18:00",
            EndTime = "19:30",
            MaxPlayers = 6
        });

        // Stranger (neither creator nor participant) attempts to invite someone
        var result = await invService.CreateInvitationAsync(stranger.Id, gameResult.Value.Id, new CreateInvitationRequest
        {
            InviteeId = invitee.Id
        });

        Assert.True(result.IsFailure);
        Assert.Equal("FORBIDDEN", result.Error.Code);
    }

    [Fact]
    public async Task Invitation_DuplicatePending_FailsWithConflict()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var playerB = new User { Id = Guid.NewGuid(), Name = "Player B", Email = "pb@test.com", PasswordHash = "hash" };
        db.Users.Add(playerB);
        await db.SaveChangesAsync();

        var notifService = new NotificationService(db, NullLogger<NotificationService>.Instance);
        var gameService = new GameService(db);
        var invService = new InvitationService(db, gameService, notifService, NullLogger<InvitationService>.Instance);

        var gameResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Duplicate Invite Match",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
            StartTime = "18:00",
            EndTime = "19:30",
            MaxPlayers = 10
        });

        var gameId = gameResult.Value.Id;

        var inv1 = await invService.CreateInvitationAsync(creatorId, gameId, new CreateInvitationRequest { InviteeId = playerB.Id });
        Assert.True(inv1.IsSuccess);

        // Attempt second identical active invitation
        var inv2 = await invService.CreateInvitationAsync(creatorId, gameId, new CreateInvitationRequest { InviteeId = playerB.Id });
        Assert.True(inv2.IsFailure);
        Assert.Equal("CONFLICT", inv2.Error.Code);
    }

    [Fact]
    public async Task Invitation_Accept_SuccessfullyJoinsMatchAndMarksAccepted()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var playerB = new User { Id = Guid.NewGuid(), Name = "Player B", Email = "pb@test.com", PasswordHash = "hash" };
        db.Users.Add(playerB);
        await db.SaveChangesAsync();

        var notifService = new NotificationService(db, NullLogger<NotificationService>.Instance);
        var gameService = new GameService(db);
        var invService = new InvitationService(db, gameService, notifService, NullLogger<InvitationService>.Instance);

        var gameResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Acceptance Match",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            StartTime = "18:00",
            EndTime = "19:30",
            MaxPlayers = 4
        });
        var gameId = gameResult.Value.Id;

        var invResult = await invService.CreateInvitationAsync(creatorId, gameId, new CreateInvitationRequest { InviteeId = playerB.Id });
        var invitationId = invResult.Value.Id;

        // Player B accepts invitation
        var acceptResult = await invService.AcceptInvitationAsync(playerB.Id, invitationId);
        Assert.True(acceptResult.IsSuccess);
        Assert.Equal(2, acceptResult.Value.CurrentPlayersCount);

        // Verify invitation status is Accepted
        var sentList = await invService.GetSentInvitationsAsync(creatorId, new PagedRequest());
        Assert.Equal("Accepted", sentList.Items.First().Status);

        // Verify creator received notification
        var creatorUnread = await notifService.GetUnreadCountAsync(creatorId);
        Assert.True(creatorUnread > 0);
    }

    [Fact]
    public async Task Invitation_Decline_MarksDeclinedAndNotifiesInviter()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var playerB = new User { Id = Guid.NewGuid(), Name = "Player B", Email = "pb@test.com", PasswordHash = "hash" };
        db.Users.Add(playerB);
        await db.SaveChangesAsync();

        var notifService = new NotificationService(db, NullLogger<NotificationService>.Instance);
        var gameService = new GameService(db);
        var invService = new InvitationService(db, gameService, notifService, NullLogger<InvitationService>.Instance);

        var gameResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Decline Match",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            StartTime = "18:00",
            EndTime = "19:30",
            MaxPlayers = 4
        });

        var invResult = await invService.CreateInvitationAsync(creatorId, gameResult.Value.Id, new CreateInvitationRequest { InviteeId = playerB.Id });

        // Player B declines
        var declineResult = await invService.DeclineInvitationAsync(playerB.Id, invResult.Value.Id);
        Assert.True(declineResult.IsSuccess);

        var sentList = await invService.GetSentInvitationsAsync(creatorId, new PagedRequest());
        Assert.Equal("Declined", sentList.Items.First().Status);
    }

    [Fact]
    public async Task Invitation_CancelledOrStartedGame_FailsGracefully()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var playerB = new User { Id = Guid.NewGuid(), Name = "Player B", Email = "pb@test.com", PasswordHash = "hash" };
        db.Users.Add(playerB);
        await db.SaveChangesAsync();

        var notifService = new NotificationService(db, NullLogger<NotificationService>.Instance);
        var gameService = new GameService(db);
        var invService = new InvitationService(db, gameService, notifService, NullLogger<InvitationService>.Instance);

        var gameResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Cancelled Match",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
            StartTime = "18:00",
            EndTime = "19:30",
            MaxPlayers = 4
        });
        var gameId = gameResult.Value.Id;

        // Cancel the game
        await gameService.CancelGameAsync(creatorId, "Client", gameId);

        // Attempt invitation to cancelled game
        var invResult = await invService.CreateInvitationAsync(creatorId, gameId, new CreateInvitationRequest { InviteeId = playerB.Id });
        Assert.True(invResult.IsFailure);
        Assert.Equal("BAD_REQUEST", invResult.Error.Code);
    }

    // ==========================================
    // 2. PLAYER CONNECTION & SOCIAL GRAPH TESTS
    // ==========================================

    [Fact]
    public async Task Connection_SendRequest_SucceedsAndDispatchesNotification()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, _, _, userA) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var userB = new User { Id = Guid.NewGuid(), Name = "User B", Email = "ub@test.com", PasswordHash = "hash" };
        db.Users.Add(userB);
        await db.SaveChangesAsync();

        var notifService = new NotificationService(db, NullLogger<NotificationService>.Instance);
        var connService = new ConnectionService(db, notifService, NullLogger<ConnectionService>.Instance);

        var sendResult = await connService.SendConnectionRequestAsync(userA, userB.Id);
        Assert.True(sendResult.IsSuccess);
        Assert.Equal("Pending", sendResult.Value.Status);
        Assert.True(sendResult.Value.IsInitiator);

        // Verify User B received notification
        var unread = await notifService.GetUnreadCountAsync(userB.Id);
        Assert.Equal(1, unread);
    }

    [Fact]
    public async Task Connection_SelfConnect_FailsWithBadRequest()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, _, _, userA) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var notifService = new NotificationService(db, NullLogger<NotificationService>.Instance);
        var connService = new ConnectionService(db, notifService, NullLogger<ConnectionService>.Instance);

        var result = await connService.SendConnectionRequestAsync(userA, userA);
        Assert.True(result.IsFailure);
        Assert.Equal("BAD_REQUEST", result.Error.Code);
    }

    [Fact]
    public async Task Connection_MutualRequest_AutomaticallyAccepts()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, _, _, userA) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var userB = new User { Id = Guid.NewGuid(), Name = "User B", Email = "ub2@test.com", PasswordHash = "hash" };
        db.Users.Add(userB);
        await db.SaveChangesAsync();

        var notifService = new NotificationService(db, NullLogger<NotificationService>.Instance);
        var connService = new ConnectionService(db, notifService, NullLogger<ConnectionService>.Instance);

        // User A requests User B
        await connService.SendConnectionRequestAsync(userA, userB.Id);

        // User B sends request back to User A -> Auto-accepts!
        var mutualResult = await connService.SendConnectionRequestAsync(userB.Id, userA);
        Assert.True(mutualResult.IsSuccess);
        Assert.Equal("Accepted", mutualResult.Value.Status);

        // Verify both can view each other in connections list
        var aConns = await connService.GetConnectionsAsync(userA, ConnectionStatus.Accepted, new PagedRequest());
        Assert.Single(aConns.Items);

        var bConns = await connService.GetConnectionsAsync(userB.Id, ConnectionStatus.Accepted, new PagedRequest());
        Assert.Single(bConns.Items);
    }

    [Fact]
    public async Task Connection_AcceptAndDecline_UpdatesStateCorrectly()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, _, _, userA) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var userB = new User { Id = Guid.NewGuid(), Name = "User B", Email = "ub3@test.com", PasswordHash = "hash" };
        db.Users.Add(userB);
        await db.SaveChangesAsync();

        var notifService = new NotificationService(db, NullLogger<NotificationService>.Instance);
        var connService = new ConnectionService(db, notifService, NullLogger<ConnectionService>.Instance);

        var sendResult = await connService.SendConnectionRequestAsync(userA, userB.Id);
        var connectionId = sendResult.Value.Id;

        // User B accepts
        var acceptResult = await connService.AcceptConnectionRequestAsync(userB.Id, connectionId);
        Assert.True(acceptResult.IsSuccess);
        Assert.Equal("Accepted", acceptResult.Value.Status);

        // Remove connection
        var removeResult = await connService.CancelOrRemoveConnectionAsync(userA, connectionId);
        Assert.True(removeResult.IsSuccess);

        var afterRemove = await connService.GetConnectionsAsync(userA, ConnectionStatus.Accepted, new PagedRequest());
        Assert.Empty(afterRemove.Items);
    }

    // ==========================================
    // 3. BLOCK / UNBLOCK SYSTEM TESTS
    // ==========================================

    [Fact]
    public async Task Block_PreventsInvitationsAndConnections_BetweenUsers()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, userA) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var userB = new User { Id = Guid.NewGuid(), Name = "User B", Email = "ublock@test.com", PasswordHash = "hash" };
        db.Users.Add(userB);
        await db.SaveChangesAsync();

        var notifService = new NotificationService(db, NullLogger<NotificationService>.Instance);
        var gameService = new GameService(db);
        var invService = new InvitationService(db, gameService, notifService, NullLogger<InvitationService>.Instance);
        var connService = new ConnectionService(db, notifService, NullLogger<ConnectionService>.Instance);

        // User A blocks User B
        var blockResult = await connService.BlockUserAsync(userA, userB.Id);
        Assert.True(blockResult.IsSuccess);

        var blockedList = await connService.GetBlockedUsersAsync(userA, new PagedRequest());
        Assert.Single(blockedList.Items);

        // 1. User A attempts to invite User B -> Forbidden
        var gameResult = await gameService.CreateGameAsync(userA, new CreateGameRequest
        {
            Title = "Block Test Match",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
            StartTime = "18:00",
            EndTime = "19:30",
            MaxPlayers = 6
        });
        var invResult = await invService.CreateInvitationAsync(userA, gameResult.Value.Id, new CreateInvitationRequest { InviteeId = userB.Id });
        Assert.True(invResult.IsFailure);
        Assert.Equal("FORBIDDEN", invResult.Error.Code);

        // 2. User B attempts to send connection request to User A -> Forbidden
        var connReqResult = await connService.SendConnectionRequestAsync(userB.Id, userA);
        Assert.True(connReqResult.IsFailure);
        Assert.Equal("FORBIDDEN", connReqResult.Error.Code);

        // 3. Unblock User B
        var unblockResult = await connService.UnblockUserAsync(userA, userB.Id);
        Assert.True(unblockResult.IsSuccess);

        var afterUnblock = await connService.GetBlockedUsersAsync(userA, new PagedRequest());
        Assert.Empty(afterUnblock.Items);
    }

    [Fact]
    public async Task Block_FiltersOutGamesHostedByBlockedUsers_FromMatchmaking()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, hostUser) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var searchingPlayer = new User { Id = Guid.NewGuid(), Name = "Player Searcher", Email = "search@test.com", PasswordHash = "hash" };
        db.Users.Add(searchingPlayer);
        await db.SaveChangesAsync();

        var gameService = new GameService(db);
        var connService = new ConnectionService(db, new NotificationService(db, NullLogger<NotificationService>.Instance), NullLogger<ConnectionService>.Instance);
        var mmService = new MatchmakingService(db);

        // Host creates an open game
        await gameService.CreateGameAsync(hostUser, new CreateGameRequest
        {
            Title = "Host Match",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            StartTime = "18:00",
            EndTime = "19:30",
            MaxPlayers = 10
        });

        // 1. Before block: recommendations include Host's game
        var recsBefore = await mmService.GetRecommendationsAsync(searchingPlayer.Id);
        Assert.True(recsBefore.IsSuccess);
        Assert.NotEmpty(recsBefore.Value);

        // 2. Player blocks Host
        await connService.BlockUserAsync(searchingPlayer.Id, hostUser);

        // 3. After block: Host's match must be filtered out
        var recsAfter = await mmService.GetRecommendationsAsync(searchingPlayer.Id);
        Assert.True(recsAfter.IsSuccess);
        Assert.Empty(recsAfter.Value);
    }

    // ==========================================
    // 4. GAME HISTORY & REPUTATION TESTS
    // ==========================================

    [Fact]
    public async Task GameHistory_RetrievesPastMatches_ForPlayerWithFilters()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, player) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var pastDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5));
        var pastGame = new Game
        {
            Id = Guid.NewGuid(),
            Title = "Historical Championship Match",
            SportType = SportType.Football,
            VenueId = venueId,
            CourtId = courtId,
            CreatorId = player,
            Date = pastDate,
            StartTime = new TimeOnly(18, 0),
            EndTime = new TimeOnly(19, 30),
            Status = GameStatus.Completed,
            MaxPlayers = 10
        };
        pastGame.Participants.Add(new GameParticipant
        {
            Id = Guid.NewGuid(),
            GameId = pastGame.Id,
            UserId = player,
            IsConfirmed = true,
            Team = "TeamA"
        });
        db.Games.Add(pastGame);
        await db.SaveChangesAsync();

        var communityService = new PlayerCommunityService(db, NullLogger<PlayerCommunityService>.Instance);

        var history = await communityService.GetPlayerGameHistoryAsync(player, player, new GameHistoryFilterRequest
        {
            SportType = "Football"
        });

        Assert.True(history.IsSuccess);
        Assert.Single(history.Value.Items);
        Assert.Equal("Historical Championship Match", history.Value.Items.First().Title);
        Assert.Equal("TeamA", history.Value.Items.First().Team);
        Assert.True(history.Value.Items.First().IsCreator);
    }

    [Fact]
    public async Task Reputation_CalculatesDeterministicScore_WithExplainableBreakdown()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, player) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        // Seed 3 completed past games
        for (int i = 1; i <= 3; i++)
        {
            var g = new Game
            {
                Id = Guid.NewGuid(),
                Title = $"Past Game {i}",
                SportType = SportType.Football,
                VenueId = venueId,
                CourtId = courtId,
                CreatorId = player,
                Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-i * 2)),
                StartTime = new TimeOnly(18, 0),
                EndTime = new TimeOnly(19, 30),
                Status = GameStatus.Completed,
                MaxPlayers = 10
            };
            g.Participants.Add(new GameParticipant { Id = Guid.NewGuid(), GameId = g.Id, UserId = player });
            db.Games.Add(g);
        }

        // Seed 1 cancelled game organized by player
        var cancelledGame = new Game
        {
            Id = Guid.NewGuid(),
            Title = "Cancelled Match",
            SportType = SportType.Football,
            VenueId = venueId,
            CourtId = courtId,
            CreatorId = player,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            StartTime = new TimeOnly(18, 0),
            EndTime = new TimeOnly(19, 30),
            Status = GameStatus.Cancelled,
            MaxPlayers = 10
        };
        db.Games.Add(cancelledGame);
        await db.SaveChangesAsync();

        var communityService = new PlayerCommunityService(db, NullLogger<PlayerCommunityService>.Instance);
        var repResult = await communityService.GetPlayerReputationAsync(player);

        Assert.True(repResult.IsSuccess);
        var rep = repResult.Value;
        Assert.Equal(3, rep.CompletedMatchesCount);
        Assert.Equal(4, rep.OrganizedMatchesCount);
        Assert.Equal(1, rep.CancelledMatchesCount);
        Assert.Equal(100.0, rep.AttendanceRate);
        Assert.True(rep.ReliabilityScore > 85.0 && rep.ReliabilityScore < 100.0);
        Assert.NotEmpty(rep.Explanation);
    }

    [Fact]
    public async Task PublicProfile_DoesNotExposeSensitiveData()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, _, _, player) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var communityService = new PlayerCommunityService(db, NullLogger<PlayerCommunityService>.Instance);
        var profileResult = await communityService.GetPublicPlayerProfileAsync(null, player);

        Assert.True(profileResult.IsSuccess);
        var p = profileResult.Value;
        Assert.Equal(player, p.UserId);
        Assert.NotEmpty(p.Name);
        Assert.NotNull(p.Reputation);
    }

    // ==========================================
    // 5. VALIDATOR TESTS
    // ==========================================

    [Fact]
    public void FluentValidators_ValidateCommunityRequestsCorrectly()
    {
        var invValidator = new CreateInvitationRequestValidator();
        var invalidInv = new CreateInvitationRequest
        {
            InviteeId = Guid.Empty,
            Message = new string('A', 600) // > 500
        };
        var invValRes = invValidator.Validate(invalidInv);
        Assert.False(invValRes.IsValid);
        Assert.Contains(invValRes.Errors, e => e.PropertyName == nameof(CreateInvitationRequest.InviteeId));
        Assert.Contains(invValRes.Errors, e => e.PropertyName == nameof(CreateInvitationRequest.Message));

        var connValidator = new SendConnectionRequestValidator();
        var invalidConn = new SendConnectionRequest { TargetUserId = Guid.Empty };
        var connValRes = connValidator.Validate(invalidConn);
        Assert.False(connValRes.IsValid);

        var blockValidator = new BlockUserRequestValidator();
        var invalidBlock = new BlockUserRequest { TargetUserId = Guid.Empty };
        var blockValRes = blockValidator.Validate(invalidBlock);
        Assert.False(blockValRes.IsValid);
    }

    // ==========================================
    // 6. CONCURRENCY & SECURITY INTEGRITY TESTS
    // ==========================================

    [Fact]
    public async Task Invitation_ConcurrentAcceptance_ProtectsGameCapacityUnderRace()
    {
        var dbName = Guid.NewGuid().ToString();
        var seedDb = TestDbContextFactory.Create(dbName);
        var (venueId, courtId, _, creatorId) = await TestDbContextFactory.SeedBasicTestDataAsync(seedDb);

        var racer1 = new User { Id = Guid.NewGuid(), Name = "Racer 1", Email = "r1@test.com", PasswordHash = "hash" };
        var racer2 = new User { Id = Guid.NewGuid(), Name = "Racer 2", Email = "r2@test.com", PasswordHash = "hash" };
        seedDb.Users.AddRange(racer1, racer2);
        await seedDb.SaveChangesAsync();

        var gameService = new GameService(seedDb);
        var notifService = new NotificationService(seedDb, NullLogger<NotificationService>.Instance);
        var invService = new InvitationService(seedDb, gameService, notifService, NullLogger<InvitationService>.Instance);

        // MaxPlayers = 2 (Only 1 spot available for Racer 1 or Racer 2)
        var createResult = await gameService.CreateGameAsync(creatorId, new CreateGameRequest
        {
            Title = "Capacity Race Match",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            StartTime = "18:00",
            EndTime = "19:30",
            MaxPlayers = 2
        });
        var gameId = createResult.Value.Id;

        var inv1 = await invService.CreateInvitationAsync(creatorId, gameId, new CreateInvitationRequest { InviteeId = racer1.Id });
        var inv2 = await invService.CreateInvitationAsync(creatorId, gameId, new CreateInvitationRequest { InviteeId = racer2.Id });

        // Concurrent acceptance from two separate threads / DbContexts
        var task1 = Task.Run(async () =>
        {
            var db1 = TestDbContextFactory.Create(dbName);
            var gs1 = new GameService(db1);
            var ns1 = new NotificationService(db1, NullLogger<NotificationService>.Instance);
            var is1 = new InvitationService(db1, gs1, ns1, NullLogger<InvitationService>.Instance);
            return await is1.AcceptInvitationAsync(racer1.Id, inv1.Value.Id);
        });

        var task2 = Task.Run(async () =>
        {
            var db2 = TestDbContextFactory.Create(dbName);
            var gs2 = new GameService(db2);
            var ns2 = new NotificationService(db2, NullLogger<NotificationService>.Instance);
            var is2 = new InvitationService(db2, gs2, ns2, NullLogger<InvitationService>.Instance);
            return await is2.AcceptInvitationAsync(racer2.Id, inv2.Value.Id);
        });

        var results = await Task.WhenAll(task1, task2);

        // Exactly one racer succeeds, and one racer is rejected
        var successCount = results.Count(r => r.IsSuccess);
        var failureCount = results.Count(r => r.IsFailure);

        Assert.Equal(1, successCount);
        Assert.Equal(1, failureCount);

        var verifyDb = TestDbContextFactory.Create(dbName);
        var verifyGs = new GameService(verifyDb);
        var finalGame = await verifyGs.GetByIdAsync(gameId);

        // Capacity is never breached
        Assert.Equal(2, finalGame.Value.CurrentPlayersCount);
        Assert.Equal("Full", finalGame.Value.Status);
    }

    [Fact]
    public async Task Security_IDOR_PreventUnauthorizedAcceptanceOrDeclineOfInvitationsAndConnections()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, courtId, _, userA) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var userB = new User { Id = Guid.NewGuid(), Name = "User B", Email = "ubidor@test.com", PasswordHash = "hash" };
        var attacker = new User { Id = Guid.NewGuid(), Name = "Attacker", Email = "attacker@test.com", PasswordHash = "hash" };
        db.Users.AddRange(userB, attacker);
        await db.SaveChangesAsync();

        var notifService = new NotificationService(db, NullLogger<NotificationService>.Instance);
        var gameService = new GameService(db);
        var invService = new InvitationService(db, gameService, notifService, NullLogger<InvitationService>.Instance);
        var connService = new ConnectionService(db, notifService, NullLogger<ConnectionService>.Instance);

        var gameResult = await gameService.CreateGameAsync(userA, new CreateGameRequest
        {
            Title = "IDOR Test Match",
            SportType = "Football",
            VenueId = venueId,
            CourtId = courtId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            StartTime = "18:00",
            EndTime = "19:30",
            MaxPlayers = 6
        });
        var invResult = await invService.CreateInvitationAsync(userA, gameResult.Value.Id, new CreateInvitationRequest { InviteeId = userB.Id });
        var connResult = await connService.SendConnectionRequestAsync(userA, userB.Id);

        // 1. Attacker attempts to accept User B's invitation -> Forbidden
        var unauthorizedInvAccept = await invService.AcceptInvitationAsync(attacker.Id, invResult.Value.Id);
        Assert.True(unauthorizedInvAccept.IsFailure);
        Assert.Equal("FORBIDDEN", unauthorizedInvAccept.Error.Code);

        // 2. Attacker attempts to decline User B's invitation -> Forbidden
        var unauthorizedInvDecline = await invService.DeclineInvitationAsync(attacker.Id, invResult.Value.Id);
        Assert.True(unauthorizedInvDecline.IsFailure);
        Assert.Equal("FORBIDDEN", unauthorizedInvDecline.Error.Code);

        // 3. Attacker attempts to cancel User A's invitation -> Forbidden
        var unauthorizedInvCancel = await invService.CancelInvitationAsync(attacker.Id, invResult.Value.Id);
        Assert.True(unauthorizedInvCancel.IsFailure);
        Assert.Equal("FORBIDDEN", unauthorizedInvCancel.Error.Code);

        // 4. Attacker attempts to accept User B's connection request -> Forbidden
        var unauthorizedConnAccept = await connService.AcceptConnectionRequestAsync(attacker.Id, connResult.Value.Id);
        Assert.True(unauthorizedConnAccept.IsFailure);
        Assert.Equal("FORBIDDEN", unauthorizedConnAccept.Error.Code);

        // 5. Attacker attempts to decline User B's connection request -> Forbidden
        var unauthorizedConnDecline = await connService.DeclineConnectionRequestAsync(attacker.Id, connResult.Value.Id);
        Assert.True(unauthorizedConnDecline.IsFailure);
        Assert.Equal("FORBIDDEN", unauthorizedConnDecline.Error.Code);
    }
}

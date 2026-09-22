using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace CourtBook.Infrastructure.Services;

public class GameService : IGameService
{
    private readonly AppDbContext _db;
    private readonly IGameLobbySender? _lobbySender;

    public GameService(AppDbContext db, IGameLobbySender? lobbySender = null)
    {
        _db = db;
        _lobbySender = lobbySender;
    }

    public async Task<Result<GameResponse>> CreateGameAsync(Guid creatorId, CreateGameRequest request)
    {
        if (!TimeOnly.TryParse(request.StartTime, out var startTime) ||
            !TimeOnly.TryParse(request.EndTime, out var endTime))
        {
            return Error.Validation("StartTime and EndTime must be in HH:mm format.");
        }

        if (startTime >= endTime)
            return Error.Validation("StartTime must be before EndTime.");

        if (request.MaxPlayers < 2 || request.MaxPlayers > 50)
            return Error.Validation("MaxPlayers must be between 2 and 50.");

        var executionStrategy = _db.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(async () =>
        {
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
            if (_db.Database.IsRelational())
            {
                transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            }

            try
            {
                var court = await _db.Courts
                    .Include(c => c.Schedules)
                    .Include(c => c.Venue)
                    .FirstOrDefaultAsync(c => c.Id == request.CourtId && c.VenueId == request.VenueId && c.IsActive);

                if (court is null)
                    return Error.NotFound("Court at specified Venue");

                // Validate court operating hours for game date in Egypt local time
                var gameStartUtc = TimeZoneHelper.CreateUtcFromEgyptDateAndTime(request.Date, startTime);
                var gameEndUtc = TimeZoneHelper.CreateUtcFromEgyptDateAndTime(request.Date, endTime);
                var localGameStart = TimeZoneHelper.ConvertUtcToEgypt(gameStartUtc);
                var dayOfWeek = localGameStart.DayOfWeek;
                var schedule = court.Schedules.FirstOrDefault(s => s.DayOfWeek == dayOfWeek);

                if (schedule is null || startTime < schedule.OpenTime || endTime > schedule.CloseTime)
                {
                    return Error.BadRequest("Game time is outside court working hours.");
                }

                // Concurrency-safe check for conflicting active bookings
                var hasBookingConflict = await _db.Bookings.AnyAsync(b =>
                    b.CourtId == request.CourtId
                    && b.Status != BookingStatus.Cancelled
                    && b.StartTime < gameEndUtc
                    && b.EndTime > gameStartUtc);

                if (hasBookingConflict)
                    return Error.Conflict("Court is already booked for this time slot.");

                // Concurrency-safe check for conflicting active community games
                var hasGameConflict = await _db.Games.AnyAsync(g =>
                    g.CourtId == request.CourtId
                    && (g.Status == GameStatus.Open || g.Status == GameStatus.Full)
                    && g.Date == request.Date
                    && g.StartTime < endTime
                    && g.EndTime > startTime);

                if (hasGameConflict)
                    return Error.Conflict("Another community match is already scheduled on this court for this time slot.");

                if (!Enum.TryParse<SportType>(request.SportType, true, out var sportType))
                    sportType = court.SportType;

                if (!Enum.TryParse<SkillLevel>(request.SkillLevel, true, out var skillLevel))
                    skillLevel = SkillLevel.AllLevels;

                if (!Enum.TryParse<AgeGroup>(request.AgeGroup, true, out var ageGroup))
                    ageGroup = AgeGroup.AllAges;

                int? minAge = request.MinAge;
                int? maxAge = request.MaxAge;

                if (ageGroup == AgeGroup.Kids) { minAge ??= 6; maxAge ??= 12; }
                else if (ageGroup == AgeGroup.Juniors) { minAge ??= 13; maxAge ??= 15; }
                else if (ageGroup == AgeGroup.Teens) { minAge ??= 16; maxAge ??= 17; }
                else if (ageGroup == AgeGroup.Adults) { minAge ??= 18; maxAge = null; }
                else if (ageGroup == AgeGroup.Custom)
                {
                    if (minAge.HasValue && maxAge.HasValue && minAge.Value > maxAge.Value)
                        return Error.Validation("MinAge cannot be greater than MaxAge.");
                }

                string? accessCode = null;
                if (request.IsPrivate)
                {
                    accessCode = !string.IsNullOrWhiteSpace(request.AccessCode)
                        ? request.AccessCode.Trim().ToUpperInvariant()
                        : GenerateAccessCode();
                }

                var game = new Game
                {
                    Id = Guid.NewGuid(),
                    Title = request.Title.Trim(),
                    SportType = sportType,
                    VenueId = request.VenueId,
                    CourtId = request.CourtId,
                    CreatorId = creatorId,
                    Date = request.Date,
                    StartTime = startTime,
                    EndTime = endTime,
                    SkillLevel = skillLevel,
                    AgeGroup = ageGroup,
                    MinAge = minAge,
                    MaxAge = maxAge,
                    MaxPlayers = request.MaxPlayers,
                    MinPlayers = Math.Min(request.MinPlayers, request.MaxPlayers),
                    PricePerPlayer = request.PricePerPlayer,
                    Status = GameStatus.Open,
                    Description = request.Description,
                    IsPrivate = request.IsPrivate,
                    AccessCode = accessCode,
                    HasTeams = request.HasTeams,
                    ConcurrencyStamp = Guid.NewGuid(),
                    CreatedAt = DateTime.UtcNow
                };

                game.Participants.Add(new GameParticipant
                {
                    Id = Guid.NewGuid(),
                    GameId = game.Id,
                    UserId = creatorId,
                    IsConfirmed = true,
                    IsReady = true, // Creator is automatically ready
                    Team = request.HasTeams ? "TeamA" : null,
                    ConcurrencyStamp = Guid.NewGuid(),
                    JoinedAt = DateTime.UtcNow
                });

                _db.Games.Add(game);
                await _db.SaveChangesAsync();

                if (transaction != null)
                {
                    await transaction.CommitAsync();
                }

                return await GetByIdAsync(game.Id, creatorId);
            }
            catch
            {
                if (transaction != null)
                {
                    await transaction.RollbackAsync();
                }
                throw;
            }
            finally
            {
                if (transaction != null)
                {
                    await transaction.DisposeAsync();
                }
            }
        });
    }

    public async Task<PagedResult<GameResponse>> SearchGamesAsync(GameSearchRequest request, Guid? currentUserId = null)
    {
        var query = _db.Games
            .AsNoTracking()
            .Include(g => g.Venue)
            .Include(g => g.Court)
            .Include(g => g.Creator)
            .Include(g => g.Participants)
                .ThenInclude(p => p.User)
                    .ThenInclude(u => u.SportSkills)
            .Include(g => g.Participants)
                .ThenInclude(p => p.User)
                    .ThenInclude(u => u.Profile)
            .AsQueryable();

        // Privacy filter: by default only show public games
        if (request.IncludePrivate != true)
        {
            query = query.Where(g => !g.IsPrivate);
        }

        if (!string.IsNullOrWhiteSpace(request.Sport) && Enum.TryParse<SportType>(request.Sport, true, out var sport))
        {
            query = query.Where(g => g.SportType == sport);
        }

        if (!string.IsNullOrWhiteSpace(request.City))
        {
            var cityTerm = request.City.Trim().ToLower();
            query = query.Where(g => g.Venue.City.ToLower().Contains(cityTerm));
        }

        if (!string.IsNullOrWhiteSpace(request.SkillLevel) && Enum.TryParse<SkillLevel>(request.SkillLevel, true, out var skill))
        {
            query = query.Where(g => g.SkillLevel == skill || g.SkillLevel == SkillLevel.AllLevels);
        }

        if (request.Date.HasValue)
        {
            query = query.Where(g => g.Date == request.Date.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.AgeGroup) && Enum.TryParse<AgeGroup>(request.AgeGroup, true, out var ageFilter))
        {
            query = query.Where(g => g.AgeGroup == ageFilter);
        }

        if (!string.IsNullOrWhiteSpace(request.Status) && Enum.TryParse<GameStatus>(request.Status, true, out var status))
        {
            query = query.Where(g => g.Status == status);
        }
        else
        {
            // By default show Open or Full games (active)
            query = query.Where(g => g.Status == GameStatus.Open || g.Status == GameStatus.Full);
        }

        query = query.OrderBy(g => g.Date).ThenBy(g => g.StartTime);

        var totalCount = await query.CountAsync();

        var games = await query
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToListAsync();

        var items = games.Select(g => MapToResponse(g, currentUserId)).ToList();

        return PagedResult<GameResponse>.From(items, totalCount, request.Page, request.PageSize);
    }

    public async Task<Result<GameResponse>> GetByIdAsync(Guid gameId, Guid? currentUserId = null)
    {
        var game = await _db.Games
            .AsNoTracking()
            .Include(g => g.Venue)
            .Include(g => g.Court)
            .Include(g => g.Creator)
            .Include(g => g.Participants)
                .ThenInclude(p => p.User)
                    .ThenInclude(u => u.SportSkills)
            .Include(g => g.Participants)
                .ThenInclude(p => p.User)
                    .ThenInclude(u => u.Profile)
            .FirstOrDefaultAsync(g => g.Id == gameId);

        if (game is null)
            return Error.NotFound("Game");

        return Result<GameResponse>.Ok(MapToResponse(game, currentUserId));
    }

    public async Task<Result> JoinGameAsync(Guid userId, Guid gameId, string? accessCode = null)
    {
        var executionStrategy = _db.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(async () =>
        {
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
            if (_db.Database.IsRelational())
            {
                transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            }

            try
            {
                var game = await _db.Games.FirstOrDefaultAsync(g => g.Id == gameId);
                if (game is null)
                    return Error.NotFound("Game");

                // Privacy check
                if (game.IsPrivate && game.CreatorId != userId)
                {
                    if (string.IsNullOrWhiteSpace(accessCode) ||
                        !string.Equals(game.AccessCode, accessCode.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        return Error.Forbidden("Invalid or missing access code for this private match.");
                    }
                }

                if (game.Status != GameStatus.Open)
                    return Error.BadRequest($"Cannot join game. Current status is {game.Status}.");

                var gameStartDtUtc = TimeZoneHelper.CreateUtcFromEgyptDateAndTime(game.Date, game.StartTime);
                if (gameStartDtUtc < DateTime.UtcNow)
                    return Error.BadRequest("Cannot join a match that has already started.");

                var currentParticipantsCount = await _db.GameParticipants.CountAsync(p => p.GameId == gameId);
                if (currentParticipantsCount >= game.MaxPlayers)
                    return Error.BadRequest("This match is already full.");

                var alreadyJoined = await _db.GameParticipants.AnyAsync(p => p.GameId == gameId && p.UserId == userId);
                if (alreadyJoined)
                    return Error.Conflict("You have already joined this match.");

                var user = await _db.Users
                    .Include(u => u.SportSkills)
                    .Include(u => u.Profile)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user is null)
                    return Error.NotFound("User");

                // Age eligibility verification
                if (game.AgeGroup != AgeGroup.AllAges || game.MinAge.HasValue || game.MaxAge.HasValue)
                {
                    if (!user.DateOfBirth.HasValue)
                    {
                        return Error.BadRequest("Please update your profile with your date of birth to join age-restricted community games.");
                    }

                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    var playerAge = today.Year - user.DateOfBirth.Value.Year;
                    if (user.DateOfBirth.Value > today.AddYears(-playerAge))
                        playerAge--;

                    int? effectiveMinAge = game.MinAge;
                    int? effectiveMaxAge = game.MaxAge;

                    switch (game.AgeGroup)
                    {
                        case AgeGroup.Kids:
                            effectiveMinAge ??= 6;
                            effectiveMaxAge ??= 12;
                            break;
                        case AgeGroup.Juniors:
                            effectiveMinAge ??= 13;
                            effectiveMaxAge ??= 15;
                            break;
                        case AgeGroup.Teens:
                            effectiveMinAge ??= 16;
                            effectiveMaxAge ??= 17;
                            break;
                        case AgeGroup.Adults:
                            effectiveMinAge ??= 18;
                            break;
                    }

                    bool isEligible = true;
                    if (effectiveMinAge.HasValue && playerAge < effectiveMinAge.Value)
                        isEligible = false;
                    if (effectiveMaxAge.HasValue && playerAge > effectiveMaxAge.Value)
                        isEligible = false;

                    if (!isEligible)
                    {
                        return Error.BadRequest("You can't join this game because your age does not meet the game's eligibility requirements.");
                    }
                }

                var participant = new GameParticipant
                {
                    Id = Guid.NewGuid(),
                    GameId = gameId,
                    UserId = userId,
                    IsConfirmed = true,
                    IsReady = false,
                    Team = null,
                    ConcurrencyStamp = Guid.NewGuid(),
                    JoinedAt = DateTime.UtcNow
                };
                _db.GameParticipants.Add(participant);

                bool isFull = (currentParticipantsCount + 1 >= game.MaxPlayers);
                if (isFull)
                {
                    game.Status = GameStatus.Full;
                }

                // Bump game concurrency stamp
                game.ConcurrencyStamp = Guid.NewGuid();

                await _db.SaveChangesAsync();

                if (transaction != null)
                {
                    await transaction.CommitAsync();
                }

                // Fire SignalR events outside transaction
                if (_lobbySender != null)
                {
                    var skill = user.SportSkills.FirstOrDefault(s => s.SportType == game.SportType);
                    var participantDto = new GameParticipantDto
                    {
                        UserId = user.Id,
                        UserName = user.Name,
                        JoinedAt = participant.JoinedAt,
                        Team = null,
                        IsReady = false,
                        SkillScore = skill?.SkillScore ?? 1000,
                        SkillLevel = skill?.SkillLevel.ToString() ?? user.Profile?.SkillLevel.ToString() ?? "Beginner"
                    };

                    _ = _lobbySender.SendPlayerJoinedAsync(gameId, participantDto);
                    if (isFull)
                    {
                        _ = _lobbySender.SendGameFullAsync(gameId);
                    }
                }

                return Result.Ok();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (transaction != null)
                {
                    await transaction.RollbackAsync();
                }
                else
                {
                    // Fallback for non-relational in-memory testing providers (which do not support real transactions)
                    try
                    {
                        _db.ChangeTracker.Clear();
                        var leftover = await _db.GameParticipants.FirstOrDefaultAsync(p => p.GameId == gameId && p.UserId == userId);
                        if (leftover != null)
                        {
                            _db.GameParticipants.Remove(leftover);
                            await _db.SaveChangesAsync();
                        }
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
                return Error.Conflict("Match capacity changed concurrently or match is now full. Please try again.");
            }
            catch
            {
                if (transaction != null)
                {
                    await transaction.RollbackAsync();
                }
                throw;
            }
            finally
            {
                if (transaction != null)
                {
                    await transaction.DisposeAsync();
                }
            }
        });
    }

    public async Task<Result> LeaveGameAsync(Guid userId, Guid gameId)
    {
        var executionStrategy = _db.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(async () =>
        {
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
            if (_db.Database.IsRelational())
            {
                transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            }

            try
            {
                var game = await _db.Games.FirstOrDefaultAsync(g => g.Id == gameId);
                if (game is null)
                    return Error.NotFound("Game");

                if (game.CreatorId == userId)
                    return Error.BadRequest("As the game organizer, you cannot leave the game. You can cancel it instead.");

                if (game.Status == GameStatus.Cancelled || game.Status == GameStatus.Completed)
                    return Error.BadRequest($"Cannot leave a match that is already {game.Status}.");

                var participant = await _db.GameParticipants
                    .Include(p => p.User)
                    .FirstOrDefaultAsync(p => p.GameId == gameId && p.UserId == userId);

                if (participant is null)
                    return Error.NotFound("Participation");

                var gameStartDtUtc = TimeZoneHelper.CreateUtcFromEgyptDateAndTime(game.Date, game.StartTime);
                if (gameStartDtUtc < DateTime.UtcNow)
                    return Error.BadRequest("Cannot leave a match that has already started.");

                var userName = participant.User?.Name ?? "Player";
                _db.GameParticipants.Remove(participant);

                if (game.Status == GameStatus.Full)
                {
                    game.Status = GameStatus.Open;
                }

                game.ConcurrencyStamp = Guid.NewGuid();
                await _db.SaveChangesAsync();

                if (transaction != null)
                {
                    await transaction.CommitAsync();
                }

                if (_lobbySender != null)
                {
                    _ = _lobbySender.SendPlayerLeftAsync(gameId, userId, userName);
                }

                return Result.Ok();
            }
            catch
            {
                if (transaction != null)
                {
                    await transaction.RollbackAsync();
                }
                throw;
            }
            finally
            {
                if (transaction != null)
                {
                    await transaction.DisposeAsync();
                }
            }
        });
    }

    public async Task<Result> CancelGameAsync(Guid userId, string userRole, Guid gameId)
    {
        var game = await _db.Games.FindAsync(gameId);
        if (game is null)
            return Error.NotFound("Game");

        if (game.CreatorId != userId && userRole != "Admin")
            return Error.Forbidden("Only the game creator or an admin can cancel this game.");

        game.Status = GameStatus.Cancelled;
        game.ConcurrencyStamp = Guid.NewGuid();
        await _db.SaveChangesAsync();

        if (_lobbySender != null)
        {
            _ = _lobbySender.SendGameCancelledAsync(gameId);
        }

        return Result.Ok();
    }

    public async Task<Result<GameResponse>> GetGameLobbyAsync(Guid userId, Guid gameId)
    {
        var game = await _db.Games
            .AsNoTracking()
            .Include(g => g.Venue)
            .Include(g => g.Court)
            .Include(g => g.Creator)
            .Include(g => g.Participants)
                .ThenInclude(p => p.User)
                    .ThenInclude(u => u.SportSkills)
            .Include(g => g.Participants)
                .ThenInclude(p => p.User)
                    .ThenInclude(u => u.Profile)
            .FirstOrDefaultAsync(g => g.Id == gameId);

        if (game is null)
            return Error.NotFound("Game");

        // If private, only participants, creator, or admin can access the lobby
        if (game.IsPrivate && game.CreatorId != userId && !game.Participants.Any(p => p.UserId == userId))
        {
            return Error.Forbidden("You do not have access to this private game lobby.");
        }

        return Result<GameResponse>.Ok(MapToResponse(game, userId));
    }

    public async Task<Result> SetPlayerReadyAsync(Guid userId, Guid gameId, bool isReady)
    {
        var participant = await _db.GameParticipants
            .Include(p => p.Game)
                .ThenInclude(g => g.Participants)
            .FirstOrDefaultAsync(p => p.GameId == gameId && p.UserId == userId);

        if (participant is null)
            return Error.NotFound("You are not a participant in this game.");

        if (participant.Game.Status != GameStatus.Open && participant.Game.Status != GameStatus.Full)
            return Error.BadRequest($"Cannot change ready state. Game is {participant.Game.Status}.");

        var gameStartDtUtc = TimeZoneHelper.CreateUtcFromEgyptDateAndTime(participant.Game.Date, participant.Game.StartTime);
        if (gameStartDtUtc < DateTime.UtcNow)
            return Error.BadRequest("Cannot change ready state for a match that has already started.");

        participant.IsReady = isReady;
        participant.ConcurrencyStamp = Guid.NewGuid();
        await _db.SaveChangesAsync();

        var allReady = participant.Game.Participants.Count >= participant.Game.MinPlayers &&
                       participant.Game.Participants.All(p => p.IsReady);

        if (_lobbySender != null)
        {
            _ = _lobbySender.SendPlayerReadyAsync(gameId, userId, isReady, allReady);
        }

        return Result.Ok();
    }

    public async Task<Result> AssignTeamAsync(Guid organizerId, Guid gameId, Guid participantUserId, string? team)
    {
        var game = await _db.Games
            .Include(g => g.Participants)
            .FirstOrDefaultAsync(g => g.Id == gameId);

        if (game is null)
            return Error.NotFound("Game");

        if (game.CreatorId != organizerId)
            return Error.Forbidden("Only the game organizer can assign teams.");

        if (game.Status != GameStatus.Open && game.Status != GameStatus.Full)
            return Error.BadRequest($"Cannot modify teams. Game is {game.Status}.");

        var gameStartDtUtc = TimeZoneHelper.CreateUtcFromEgyptDateAndTime(game.Date, game.StartTime);
        if (gameStartDtUtc < DateTime.UtcNow)
            return Error.BadRequest("Cannot modify teams for a match that has already started.");

        var participant = game.Participants.FirstOrDefault(p => p.UserId == participantUserId);
        if (participant is null)
            return Error.NotFound("Player is not a participant in this game.");

        if (!string.IsNullOrWhiteSpace(team) && team != "TeamA" && team != "TeamB")
            return Error.Validation("Team must be either 'TeamA', 'TeamB', or null.");

        participant.Team = string.IsNullOrWhiteSpace(team) ? null : team;
        participant.ConcurrencyStamp = Guid.NewGuid();
        await _db.SaveChangesAsync();

        return Result.Ok();
    }

    public async Task<Result<BalanceTeamsResponse>> BalanceTeamsAsync(Guid organizerId, Guid gameId)
    {
        var game = await _db.Games
            .Include(g => g.Participants)
                .ThenInclude(p => p.User)
                    .ThenInclude(u => u.SportSkills)
            .Include(g => g.Participants)
                .ThenInclude(p => p.User)
                    .ThenInclude(u => u.Profile)
            .FirstOrDefaultAsync(g => g.Id == gameId);

        if (game is null)
            return Error.NotFound("Game");

        if (game.CreatorId != organizerId)
            return Error.Forbidden("Only the game organizer can balance teams.");

        if (game.Status != GameStatus.Open && game.Status != GameStatus.Full)
            return Error.BadRequest($"Cannot balance teams. Game is {game.Status}.");

        var gameStartDtUtc = TimeZoneHelper.CreateUtcFromEgyptDateAndTime(game.Date, game.StartTime);
        if (gameStartDtUtc < DateTime.UtcNow)
            return Error.BadRequest("Cannot balance teams for a match that has already started.");

        if (game.Participants.Count < 2)
            return Error.BadRequest("At least 2 players are required to balance teams.");

        // Resolve skill score for each player
        var ratedPlayers = game.Participants.Select(p =>
        {
            var sportSkill = p.User?.SportSkills.FirstOrDefault(s => s.SportType == game.SportType);
            int score = sportSkill?.SkillScore ?? p.User?.Profile?.SkillLevel switch
            {
                SkillLevel.Beginner => 1000,
                SkillLevel.Intermediate => 1500,
                SkillLevel.Advanced => 2000,
                _ => 1000
            };

            return new
            {
                Participant = p,
                SkillScore = score,
                SkillLevelStr = sportSkill?.SkillLevel.ToString() ?? p.User?.Profile?.SkillLevel.ToString() ?? "Beginner"
            };
        })
        .OrderByDescending(x => x.SkillScore)
        .ThenBy(x => x.Participant.JoinedAt)
        .ToList();

        // Deterministic Snake-Draft algorithm: A, B, B, A, A, B, B, A ...
        var teamAPlayers = new List<GameParticipantDto>();
        var teamBPlayers = new List<GameParticipantDto>();
        int teamAScore = 0;
        int teamBScore = 0;

        for (int i = 0; i < ratedPlayers.Count; i++)
        {
            var item = ratedPlayers[i];
            // In snake draft of size 4: indices 0,3 -> Team A, indices 1,2 -> Team B
            // i % 4: 0 -> A, 1 -> B, 2 -> B, 3 -> A
            bool assignToTeamA = (i % 4 == 0) || (i % 4 == 3);

            string assignedTeam = assignToTeamA ? "TeamA" : "TeamB";
            item.Participant.Team = assignedTeam;
            item.Participant.ConcurrencyStamp = Guid.NewGuid();

            var dto = new GameParticipantDto
            {
                UserId = item.Participant.UserId,
                UserName = item.Participant.User?.Name ?? "Player",
                JoinedAt = item.Participant.JoinedAt,
                Team = assignedTeam,
                IsReady = item.Participant.IsReady,
                SkillScore = item.SkillScore,
                SkillLevel = item.SkillLevelStr
            };

            if (assignToTeamA)
            {
                teamAPlayers.Add(dto);
                teamAScore += item.SkillScore;
            }
            else
            {
                teamBPlayers.Add(dto);
                teamBScore += item.SkillScore;
            }
        }

        await _db.SaveChangesAsync();

        var response = new BalanceTeamsResponse
        {
            GameId = gameId,
            TeamAName = "Team A",
            TeamBName = "Team B",
            TeamATotalScore = teamAScore,
            TeamBTotalScore = teamBScore,
            TeamAPlayers = teamAPlayers,
            TeamBPlayers = teamBPlayers
        };

        if (_lobbySender != null)
        {
            _ = _lobbySender.SendTeamsUpdatedAsync(gameId, response);
        }

        return Result<BalanceTeamsResponse>.Ok(response);
    }

    private static string GenerateAccessCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return RandomNumberGenerator.GetString(chars, 6);
    }

    private static GameResponse MapToResponse(Game g, Guid? currentUserId = null)
    {
        string ageDisplay = g.AgeGroup switch
        {
            AgeGroup.Kids => "Kids (6–12 yrs)",
            AgeGroup.Juniors => "Juniors (13–15 yrs)",
            AgeGroup.Teens => "Teens (16–17 yrs)",
            AgeGroup.Adults => "Adults (18+)",
            AgeGroup.Custom => (g.MinAge.HasValue && g.MaxAge.HasValue)
                ? $"{g.MinAge}–{g.MaxAge} yrs"
                : g.MinAge.HasValue ? $"{g.MinAge}+ yrs"
                : g.MaxAge.HasValue ? $"Up to {g.MaxAge} yrs"
                : "All Ages",
            _ => "All Ages"
        };

        bool isCreator = currentUserId.HasValue && (g.CreatorId == currentUserId.Value);
        bool isParticipant = currentUserId.HasValue && (g.Participants?.Any(p => p.UserId == currentUserId.Value) == true);

        // Security: Private games hide participant rosters from unauthorized non-participants
        bool canViewParticipants = !g.IsPrivate || isCreator || isParticipant;

        var participants = canViewParticipants && g.Participants != null
            ? g.Participants.Select(p =>
            {
                var sportSkill = p.User?.SportSkills?.FirstOrDefault(s => s.SportType == g.SportType);
                int score = sportSkill?.SkillScore ?? p.User?.Profile?.SkillLevel switch
                {
                    SkillLevel.Beginner => 1000,
                    SkillLevel.Intermediate => 1500,
                    SkillLevel.Advanced => 2000,
                    _ => 1000
                };

                return new GameParticipantDto
                {
                    UserId = p.UserId,
                    UserName = p.User?.Name ?? "Player",
                    JoinedAt = p.JoinedAt,
                    Team = p.Team,
                    IsReady = p.IsReady,
                    SkillScore = score,
                    SkillLevel = sportSkill?.SkillLevel.ToString() ?? p.User?.Profile?.SkillLevel.ToString() ?? "Beginner"
                };
            }).ToList()
            : [];

        // Security: Access code is ONLY exposed to the game creator/organizer
        string? exposedAccessCode = (g.IsPrivate && isCreator) ? g.AccessCode : null;

        return new GameResponse
        {
            Id = g.Id,
            Title = g.Title,
            SportType = g.SportType.ToString(),
            VenueId = g.VenueId,
            VenueName = g.Venue?.Name ?? string.Empty,
            VenueCity = g.Venue?.City ?? string.Empty,
            CourtId = g.CourtId,
            CourtName = g.Court?.Name ?? string.Empty,
            CreatorId = g.CreatorId,
            CreatorName = g.Creator?.Name ?? string.Empty,
            Date = g.Date,
            StartTime = g.StartTime.ToString("HH:mm"),
            EndTime = g.EndTime.ToString("HH:mm"),
            SkillLevel = g.SkillLevel.ToString(),
            AgeGroup = g.AgeGroup.ToString(),
            MinAge = g.MinAge,
            MaxAge = g.MaxAge,
            AgeDisplay = ageDisplay,
            MaxPlayers = g.MaxPlayers,
            MinPlayers = g.MinPlayers,
            CurrentPlayersCount = g.Participants?.Count ?? 0,
            PricePerPlayer = g.PricePerPlayer,
            Status = g.Status.ToString(),
            Description = g.Description,
            IsPrivate = g.IsPrivate,
            AccessCode = exposedAccessCode,
            HasTeams = g.HasTeams,
            AllPlayersReady = participants.Count >= g.MinPlayers && participants.All(p => p.IsReady),
            Participants = participants,
            CreatedAt = g.CreatedAt
        };
    }
}

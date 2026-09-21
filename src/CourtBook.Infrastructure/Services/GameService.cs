using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Infrastructure.Services;

public class GameService : IGameService
{
    private readonly AppDbContext _db;

    public GameService(AppDbContext db)
    {
        _db = db;
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
                    CreatedAt = DateTime.UtcNow
                };

                game.Participants.Add(new GameParticipant
                {
                    Id = Guid.NewGuid(),
                    GameId = game.Id,
                    UserId = creatorId,
                    IsConfirmed = true,
                    JoinedAt = DateTime.UtcNow
                });

                _db.Games.Add(game);
                await _db.SaveChangesAsync();

                if (transaction != null)
                {
                    await transaction.CommitAsync();
                }

                return await GetByIdAsync(game.Id);
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

    public async Task<PagedResult<GameResponse>> SearchGamesAsync(GameSearchRequest request)
    {
        var query = _db.Games
            .AsNoTracking()
            .Include(g => g.Venue)
            .Include(g => g.Court)
            .Include(g => g.Creator)
            .Include(g => g.Participants)
                .ThenInclude(p => p.User)
            .AsQueryable();

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

        var items = await query
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(g => MapToResponse(g))
            .ToListAsync();

        return PagedResult<GameResponse>.From(items, totalCount, request.Page, request.PageSize);
    }

    public async Task<Result<GameResponse>> GetByIdAsync(Guid gameId)
    {
        var game = await _db.Games
            .AsNoTracking()
            .Include(g => g.Venue)
            .Include(g => g.Court)
            .Include(g => g.Creator)
            .Include(g => g.Participants)
                .ThenInclude(p => p.User)
            .FirstOrDefaultAsync(g => g.Id == gameId);

        if (game is null)
            return Error.NotFound("Game");

        return Result<GameResponse>.Ok(MapToResponse(game));
    }

    public async Task<Result> JoinGameAsync(Guid userId, Guid gameId)
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

                var user = await _db.Users.FindAsync(userId);
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
                    JoinedAt = DateTime.UtcNow
                };
                _db.GameParticipants.Add(participant);

                if (currentParticipantsCount + 1 >= game.MaxPlayers)
                {
                    game.Status = GameStatus.Full;
                }

                await _db.SaveChangesAsync();

                if (transaction != null)
                {
                    await transaction.CommitAsync();
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

                var participant = await _db.GameParticipants.FirstOrDefaultAsync(p => p.GameId == gameId && p.UserId == userId);
                if (participant is null)
                    return Error.NotFound("Participation");

                var gameStartDtUtc = TimeZoneHelper.CreateUtcFromEgyptDateAndTime(game.Date, game.StartTime);
                if (gameStartDtUtc < DateTime.UtcNow)
                    return Error.BadRequest("Cannot leave a match that has already started.");

                _db.GameParticipants.Remove(participant);

                if (game.Status == GameStatus.Full)
                {
                    game.Status = GameStatus.Open;
                }

                await _db.SaveChangesAsync();

                if (transaction != null)
                {
                    await transaction.CommitAsync();
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
        await _db.SaveChangesAsync();
        return Result.Ok();
    }

    private static GameResponse MapToResponse(Game g)
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
            CreatedAt = g.CreatedAt,
            Participants = g.Participants?.Select(p => new GameParticipantDto
            {
                UserId = p.UserId,
                UserName = p.User?.Name ?? "Player",
                JoinedAt = p.JoinedAt
            }).ToList() ?? []
        };
    }
}

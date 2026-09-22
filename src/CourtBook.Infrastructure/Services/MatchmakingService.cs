using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Infrastructure.Services;

public class MatchmakingService : IMatchmakingService
{
    private readonly AppDbContext _db;

    public MatchmakingService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<List<MatchmakingRecommendationDto>>> GetRecommendationsAsync(Guid userId, MatchmakingQueryRequest? query = null)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.Profile)
            .Include(u => u.Preference)
            .Include(u => u.SportSkills)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user is null)
            return Error.NotFound("User");

        var prefSports = (user.Preference?.PreferredSports ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var prefCities = (user.Preference?.PreferredCities ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var prefDays = (user.Preference?.PreferredDays ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var prefTimes = (user.Preference?.PreferredTimeOfDay ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var userSkillsMap = user.SportSkills
            .ToDictionary(s => s.SportType, s => s);

        var nowEgypt = TimeZoneHelper.ConvertUtcToEgypt(DateTime.UtcNow);
        var today = DateOnly.FromDateTime(nowEgypt);
        var nowTime = TimeOnly.FromDateTime(nowEgypt);

        // Base candidate games: Open, not private, player has not joined, not full, future or today
        var gamesQuery = _db.Games
            .AsNoTracking()
            .Include(g => g.Venue)
            .Include(g => g.Court)
            .Include(g => g.Creator)
            .Include(g => g.Participants)
                .ThenInclude(p => p.User)
            .Where(g => g.Status == GameStatus.Open && !g.IsPrivate)
            .Where(g => g.Date > today || (g.Date == today && g.StartTime > nowTime))
            .Where(g => !g.Participants.Any(p => p.UserId == userId))
            .Where(g => g.Participants.Count < g.MaxPlayers)
            .AsQueryable();

        // Optional query overrides
        if (!string.IsNullOrWhiteSpace(query?.Sport) && Enum.TryParse<SportType>(query.Sport, true, out var sportFilter))
        {
            gamesQuery = gamesQuery.Where(g => g.SportType == sportFilter);
        }

        if (!string.IsNullOrWhiteSpace(query?.City))
        {
            var cityTerm = query.City.Trim().ToLower();
            gamesQuery = gamesQuery.Where(g => g.Venue.City.ToLower().Contains(cityTerm));
        }

        if (query?.Date.HasValue == true)
        {
            gamesQuery = gamesQuery.Where(g => g.Date == query.Date.Value);
        }

        if (!string.IsNullOrWhiteSpace(query?.SkillLevel) && Enum.TryParse<SkillLevel>(query.SkillLevel, true, out var skillFilter))
        {
            gamesQuery = gamesQuery.Where(g => g.SkillLevel == skillFilter || g.SkillLevel == SkillLevel.AllLevels);
        }

        var candidateGames = await gamesQuery.ToListAsync();

        // Calculate player age for eligibility filtering
        int? playerAge = null;
        if (user.DateOfBirth.HasValue)
        {
            playerAge = today.Year - user.DateOfBirth.Value.Year;
            if (user.DateOfBirth.Value > today.AddYears(-playerAge.Value))
                playerAge--;
        }

        var recommendations = new List<MatchmakingRecommendationDto>();

        foreach (var game in candidateGames)
        {
            // Strict UTC start check
            var gameStartUtc = TimeZoneHelper.CreateUtcFromEgyptDateAndTime(game.Date, game.StartTime);
            if (gameStartUtc <= DateTime.UtcNow)
                continue;

            // Age check filter
            if (!IsAgeEligible(game, playerAge))
                continue;

            // 1. Sport Compatibility (max 40 pts)
            double sportScore;
            string sportReason;
            var sportName = game.SportType.ToString();
            bool isPrefSport = prefSports.Any(s => string.Equals(s, sportName, StringComparison.OrdinalIgnoreCase));
            bool hasSportSkill = userSkillsMap.ContainsKey(game.SportType);

            if (isPrefSport)
            {
                sportScore = 40;
                sportReason = $"Preferred sport ({sportName})";
            }
            else if (hasSportSkill)
            {
                sportScore = 30;
                sportReason = $"Registered skill in {sportName}";
            }
            else if (prefSports.Count == 0 && (user.Profile?.PreferredSport == null || user.Profile.PreferredSport == game.SportType))
            {
                sportScore = 25;
                sportReason = $"General sport match ({sportName})";
            }
            else
            {
                sportScore = 10;
                sportReason = $"Alternative sport ({sportName})";
            }

            // 2. Skill Level Compatibility (max 25 pts)
            double skillScore;
            string skillReason;
            var userSkillLevel = userSkillsMap.TryGetValue(game.SportType, out var spSkill)
                ? spSkill.SkillLevel
                : (user.Preference?.PreferredSkillLevel ?? user.Profile?.SkillLevel ?? SkillLevel.Beginner);

            if (game.SkillLevel == SkillLevel.AllLevels)
            {
                skillScore = 20;
                skillReason = "All skill levels welcome";
            }
            else if (game.SkillLevel == userSkillLevel)
            {
                skillScore = 25;
                skillReason = $"Exact skill match ({game.SkillLevel})";
            }
            else
            {
                int levelDiff = Math.Abs((int)game.SkillLevel - (int)userSkillLevel);
                if (levelDiff == 1)
                {
                    skillScore = 12;
                    skillReason = $"Compatible skill gap ({userSkillLevel} vs {game.SkillLevel})";
                }
                else
                {
                    skillScore = 0;
                    skillReason = $"Skill mismatch ({userSkillLevel} vs {game.SkillLevel})";
                }
            }

            // 3. Time / Day Compatibility (max 20 pts)
            double timeScore = 0;
            var timeReasons = new List<string>();
            var gameDayName = game.Date.DayOfWeek.ToString();
            bool dayMatched = prefDays.Any(d => string.Equals(d, gameDayName, StringComparison.OrdinalIgnoreCase));

            if (dayMatched)
            {
                timeScore += 10;
                timeReasons.Add($"Preferred day ({gameDayName})");
            }
            else if (prefDays.Count == 0)
            {
                timeScore += 5;
            }

            var timeSlotName = GetTimeSlot(game.StartTime);
            bool timeSlotMatched = prefTimes.Any(t => string.Equals(t, timeSlotName, StringComparison.OrdinalIgnoreCase));

            if (timeSlotMatched)
            {
                timeScore += 10;
                timeReasons.Add($"Preferred time of day ({timeSlotName})");
            }
            else if (prefTimes.Count == 0)
            {
                timeScore += 5;
            }

            string timeReason = timeReasons.Count > 0 ? string.Join(", ", timeReasons) : "Flexible timing";

            // 4. Location / City Compatibility (max 15 pts)
            double locationScore;
            string locationReason;
            var venueCity = game.Venue?.City ?? string.Empty;
            bool cityMatched = prefCities.Any(c => string.Equals(c, venueCity, StringComparison.OrdinalIgnoreCase));

            if (cityMatched)
            {
                locationScore = 15;
                locationReason = $"City matched ({venueCity})";
            }
            else if (prefCities.Count == 0)
            {
                locationScore = 8;
                locationReason = $"Location available ({venueCity})";
            }
            else
            {
                locationScore = 0;
                locationReason = $"Different location ({venueCity})";
            }

            var breakdown = new MatchScoreBreakdownDto
            {
                SportScore = sportScore,
                SkillScore = skillScore,
                TimeScore = timeScore,
                LocationScore = locationScore
            };

            var totalScore = breakdown.TotalScore;
            var explanation = $"{sportReason} (+{sportScore}) | {skillReason} (+{skillScore}) | {timeReason} (+{timeScore}) | {locationReason} (+{locationScore})";

            recommendations.Add(new MatchmakingRecommendationDto
            {
                Game = MapGameToResponse(game),
                MatchScore = Math.Round(totalScore, 1),
                Breakdown = breakdown,
                Explanation = explanation
            });
        }

        // Sort descending by match score, then date, then time
        var sorted = recommendations
            .OrderByDescending(r => r.MatchScore)
            .ThenBy(r => r.Game.Date)
            .ThenBy(r => r.Game.StartTime)
            .ToList();

        return Result<List<MatchmakingRecommendationDto>>.Ok(sorted);
    }

    public async Task<Result<PlayerPreferenceDto>> GetPlayerPreferenceAsync(Guid userId)
    {
        var pref = await _db.PlayerPreferences.FirstOrDefaultAsync(p => p.UserId == userId);
        if (pref is null)
        {
            return Result<PlayerPreferenceDto>.Ok(new PlayerPreferenceDto
            {
                UserId = userId,
                UpdatedAt = DateTime.UtcNow
            });
        }

        return Result<PlayerPreferenceDto>.Ok(MapPreferenceToDto(pref));
    }

    public async Task<Result<PlayerPreferenceDto>> UpdatePlayerPreferenceAsync(Guid userId, UpdatePlayerPreferenceRequest request)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            return Error.NotFound("User");

        var pref = await _db.PlayerPreferences.FirstOrDefaultAsync(p => p.UserId == userId);
        if (pref is null)
        {
            pref = new PlayerPreference
            {
                Id = Guid.NewGuid(),
                UserId = userId
            };
            _db.PlayerPreferences.Add(pref);
        }

        if (request.PreferredSports != null)
            pref.PreferredSports = string.Join(",", request.PreferredSports.Where(s => !string.IsNullOrWhiteSpace(s)));

        if (request.PreferredCities != null)
            pref.PreferredCities = string.Join(",", request.PreferredCities.Where(c => !string.IsNullOrWhiteSpace(c)));

        if (request.PreferredDays != null)
            pref.PreferredDays = string.Join(",", request.PreferredDays.Where(d => !string.IsNullOrWhiteSpace(d)));

        if (request.PreferredTimeOfDay != null)
            pref.PreferredTimeOfDay = string.Join(",", request.PreferredTimeOfDay.Where(t => !string.IsNullOrWhiteSpace(t)));

        if (request.PreferredGameType != null)
            pref.PreferredGameType = request.PreferredGameType.Trim();

        if (request.MaxDistanceKm.HasValue)
            pref.MaxDistanceKm = Math.Clamp(request.MaxDistanceKm.Value, 1, 500);

        if (!string.IsNullOrWhiteSpace(request.PreferredSkillLevel) &&
            Enum.TryParse<SkillLevel>(request.PreferredSkillLevel, true, out var skillLevel))
        {
            pref.PreferredSkillLevel = skillLevel;
        }

        pref.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Result<PlayerPreferenceDto>.Ok(MapPreferenceToDto(pref));
    }

    public async Task<Result<List<PlayerSportSkillDto>>> GetPlayerSkillsAsync(Guid userId)
    {
        var skills = await _db.PlayerSportSkills
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.SportType)
            .Select(s => new PlayerSportSkillDto
            {
                Id = s.Id,
                SportType = s.SportType.ToString(),
                SkillLevel = s.SkillLevel.ToString(),
                SkillScore = s.SkillScore,
                MatchesPlayed = s.MatchesPlayed,
                UpdatedAt = s.UpdatedAt
            })
            .ToListAsync();

        return Result<List<PlayerSportSkillDto>>.Ok(skills);
    }

    public async Task<Result<PlayerSportSkillDto>> UpsertPlayerSkillAsync(Guid userId, UpsertPlayerSportSkillRequest request)
    {
        if (!Enum.TryParse<SportType>(request.SportType, true, out var sportType))
            return Error.Validation("Invalid SportType.");

        if (!Enum.TryParse<SkillLevel>(request.SkillLevel, true, out var skillLevel))
            skillLevel = SkillLevel.Beginner;

        int score = request.SkillScore ?? skillLevel switch
        {
            SkillLevel.Beginner => 1000,
            SkillLevel.Intermediate => 1500,
            SkillLevel.Advanced => 2000,
            _ => 1000
        };

        score = Math.Clamp(score, 500, 3000);

        var skill = await _db.PlayerSportSkills
            .FirstOrDefaultAsync(s => s.UserId == userId && s.SportType == sportType);

        if (skill is null)
        {
            skill = new PlayerSportSkill
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SportType = sportType,
                SkillLevel = skillLevel,
                SkillScore = score,
                MatchesPlayed = 0,
                UpdatedAt = DateTime.UtcNow
            };
            _db.PlayerSportSkills.Add(skill);
        }
        else
        {
            skill.SkillLevel = skillLevel;
            skill.SkillScore = score;
            skill.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        return Result<PlayerSportSkillDto>.Ok(new PlayerSportSkillDto
        {
            Id = skill.Id,
            SportType = skill.SportType.ToString(),
            SkillLevel = skill.SkillLevel.ToString(),
            SkillScore = skill.SkillScore,
            MatchesPlayed = skill.MatchesPlayed,
            UpdatedAt = skill.UpdatedAt
        });
    }

    private static bool IsAgeEligible(Game game, int? playerAge)
    {
        if (game.AgeGroup == AgeGroup.AllAges && !game.MinAge.HasValue && !game.MaxAge.HasValue)
            return true;

        if (!playerAge.HasValue)
            return false;

        int? effectiveMinAge = game.MinAge;
        int? effectiveMaxAge = game.MaxAge;

        switch (game.AgeGroup)
        {
            case AgeGroup.Kids: effectiveMinAge ??= 6; effectiveMaxAge ??= 12; break;
            case AgeGroup.Juniors: effectiveMinAge ??= 13; effectiveMaxAge ??= 15; break;
            case AgeGroup.Teens: effectiveMinAge ??= 16; effectiveMaxAge ??= 17; break;
            case AgeGroup.Adults: effectiveMinAge ??= 18; break;
        }

        if (effectiveMinAge.HasValue && playerAge.Value < effectiveMinAge.Value)
            return false;

        if (effectiveMaxAge.HasValue && playerAge.Value > effectiveMaxAge.Value)
            return false;

        return true;
    }

    private static string GetTimeSlot(TimeOnly time)
    {
        if (time.Hour is >= 6 and < 12) return "Morning";
        if (time.Hour is >= 12 and < 17) return "Afternoon";
        if (time.Hour is >= 17 and < 22) return "Evening";
        return "Night";
    }

    private static PlayerPreferenceDto MapPreferenceToDto(PlayerPreference p)
    {
        return new PlayerPreferenceDto
        {
            UserId = p.UserId,
            PreferredSports = p.PreferredSports.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            PreferredCities = p.PreferredCities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            PreferredDays = p.PreferredDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            PreferredTimeOfDay = p.PreferredTimeOfDay.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            PreferredGameType = p.PreferredGameType,
            MaxDistanceKm = p.MaxDistanceKm,
            PreferredSkillLevel = p.PreferredSkillLevel?.ToString(),
            UpdatedAt = p.UpdatedAt
        };
    }

    private static GameResponse MapGameToResponse(Game g)
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

        var participants = g.Participants?.Select(p => new GameParticipantDto
        {
            UserId = p.UserId,
            UserName = p.User?.Name ?? "Player",
            JoinedAt = p.JoinedAt,
            Team = p.Team,
            IsReady = p.IsReady
        }).ToList() ?? [];

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
            CurrentPlayersCount = participants.Count,
            PricePerPlayer = g.PricePerPlayer,
            Status = g.Status.ToString(),
            Description = g.Description,
            IsPrivate = g.IsPrivate,
            AccessCode = null, // Security: never expose access codes in public recommendations
            HasTeams = g.HasTeams,
            AllPlayersReady = participants.Count >= g.MinPlayers && participants.All(p => p.IsReady),
            Participants = participants,
            CreatedAt = g.CreatedAt
        };
    }
}

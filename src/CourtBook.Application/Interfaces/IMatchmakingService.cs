using CourtBook.Application.Common;
using CourtBook.Application.DTOs;

namespace CourtBook.Application.Interfaces;

public interface IMatchmakingService
{
    /// <summary>
    /// Computes deterministic, explainable game recommendations for a user based on
    /// sport preferences, sport-specific skill levels, timing, and geographic location.
    /// </summary>
    Task<Result<List<MatchmakingRecommendationDto>>> GetRecommendationsAsync(Guid userId, MatchmakingQueryRequest? query = null);

    /// <summary>
    /// Gets player preferences for the specified user.
    /// </summary>
    Task<Result<PlayerPreferenceDto>> GetPlayerPreferenceAsync(Guid userId);

    /// <summary>
    /// Updates player preferences for matchmaking.
    /// </summary>
    Task<Result<PlayerPreferenceDto>> UpdatePlayerPreferenceAsync(Guid userId, UpdatePlayerPreferenceRequest request);

    /// <summary>
    /// Gets all sport-specific skill profiles registered by the user.
    /// </summary>
    Task<Result<List<PlayerSportSkillDto>>> GetPlayerSkillsAsync(Guid userId);

    /// <summary>
    /// Creates or updates a sport-specific skill rating for the user.
    /// </summary>
    Task<Result<PlayerSportSkillDto>> UpsertPlayerSkillAsync(Guid userId, UpsertPlayerSportSkillRequest request);
}

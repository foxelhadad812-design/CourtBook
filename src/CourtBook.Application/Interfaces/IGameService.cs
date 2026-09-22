using CourtBook.Application.Common;
using CourtBook.Application.DTOs;

namespace CourtBook.Application.Interfaces;

public interface IGameService
{
    Task<Result<GameResponse>> CreateGameAsync(Guid creatorId, CreateGameRequest request);
    Task<PagedResult<GameResponse>> SearchGamesAsync(GameSearchRequest request, Guid? currentUserId = null);
    Task<Result<GameResponse>> GetByIdAsync(Guid gameId, Guid? currentUserId = null);
    Task<Result> JoinGameAsync(Guid userId, Guid gameId, string? accessCode = null);
    Task<Result> LeaveGameAsync(Guid userId, Guid gameId);
    Task<Result> CancelGameAsync(Guid userId, string userRole, Guid gameId);

    // Lobby, Teams & Ready State
    Task<Result<GameResponse>> GetGameLobbyAsync(Guid userId, Guid gameId);
    Task<Result> SetPlayerReadyAsync(Guid userId, Guid gameId, bool isReady);
    Task<Result> AssignTeamAsync(Guid organizerId, Guid gameId, Guid participantUserId, string? team);
    Task<Result<BalanceTeamsResponse>> BalanceTeamsAsync(Guid organizerId, Guid gameId);
}

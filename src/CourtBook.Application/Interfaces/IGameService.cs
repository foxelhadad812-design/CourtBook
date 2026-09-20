using CourtBook.Application.Common;
using CourtBook.Application.DTOs;

namespace CourtBook.Application.Interfaces;

public interface IGameService
{
    Task<Result<GameResponse>> CreateGameAsync(Guid creatorId, CreateGameRequest request);
    Task<PagedResult<GameResponse>> SearchGamesAsync(GameSearchRequest request);
    Task<Result<GameResponse>> GetByIdAsync(Guid gameId);
    Task<Result> JoinGameAsync(Guid userId, Guid gameId);
    Task<Result> LeaveGameAsync(Guid userId, Guid gameId);
    Task<Result> CancelGameAsync(Guid userId, string userRole, Guid gameId);
}

using CourtBook.Application.DTOs;

namespace CourtBook.Application.Interfaces;

public interface IGameLobbySender
{
    Task SendPlayerJoinedAsync(Guid gameId, GameParticipantDto participant, CancellationToken cancellationToken = default);
    Task SendPlayerLeftAsync(Guid gameId, Guid userId, string userName, CancellationToken cancellationToken = default);
    Task SendGameFullAsync(Guid gameId, CancellationToken cancellationToken = default);
    Task SendPlayerReadyAsync(Guid gameId, Guid userId, bool isReady, bool allPlayersReady, CancellationToken cancellationToken = default);
    Task SendTeamsUpdatedAsync(Guid gameId, BalanceTeamsResponse teams, CancellationToken cancellationToken = default);
    Task SendGameCancelledAsync(Guid gameId, CancellationToken cancellationToken = default);
}

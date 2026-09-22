using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Domain.Enums;

namespace CourtBook.Application.Interfaces;

public interface IConnectionService
{
    Task<Result<PlayerConnectionDto>> SendConnectionRequestAsync(Guid requesterId, Guid targetUserId);
    Task<Result<PlayerConnectionDto>> AcceptConnectionRequestAsync(Guid userId, Guid connectionId);
    Task<Result> DeclineConnectionRequestAsync(Guid userId, Guid connectionId);
    Task<Result> CancelOrRemoveConnectionAsync(Guid userId, Guid targetUserIdOrConnectionId);
    Task<Result> BlockUserAsync(Guid callerId, Guid targetUserId);
    Task<Result> UnblockUserAsync(Guid callerId, Guid targetUserId);
    Task<PagedResult<PlayerConnectionDto>> GetConnectionsAsync(Guid userId, ConnectionStatus? status, PagedRequest request);
    Task<PagedResult<PlayerConnectionDto>> GetBlockedUsersAsync(Guid userId, PagedRequest request);
}

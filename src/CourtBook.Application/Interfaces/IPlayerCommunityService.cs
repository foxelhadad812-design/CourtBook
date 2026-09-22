using CourtBook.Application.Common;
using CourtBook.Application.DTOs;

namespace CourtBook.Application.Interfaces;

public interface IPlayerCommunityService
{
    Task<Result<PagedResult<GameHistoryItemDto>>> GetPlayerGameHistoryAsync(Guid callerId, Guid targetUserId, GameHistoryFilterRequest request);
    Task<Result<PlayerReputationDto>> GetPlayerReputationAsync(Guid userId);
    Task<Result<PublicPlayerProfileDto>> GetPublicPlayerProfileAsync(Guid? callerId, Guid targetUserId);
    Task<PagedResult<PublicPlayerProfileDto>> SearchPlayersAsync(Guid callerId, SearchPlayersRequest request);
}

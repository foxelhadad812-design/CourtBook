using CourtBook.Application.Common;
using CourtBook.Application.DTOs;

namespace CourtBook.Application.Interfaces;

public interface IInvitationService
{
    Task<Result<GameInvitationDto>> CreateInvitationAsync(Guid inviterId, Guid gameId, CreateInvitationRequest request);
    Task<PagedResult<GameInvitationDto>> GetReceivedInvitationsAsync(Guid userId, PagedRequest request);
    Task<PagedResult<GameInvitationDto>> GetSentInvitationsAsync(Guid userId, PagedRequest request);
    Task<Result<GameResponse>> AcceptInvitationAsync(Guid userId, Guid invitationId);
    Task<Result> DeclineInvitationAsync(Guid userId, Guid invitationId);
    Task<Result> CancelInvitationAsync(Guid userId, Guid invitationId);
}

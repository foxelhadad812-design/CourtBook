using CourtBook.Application.Common;
using CourtBook.Application.DTOs;

namespace CourtBook.Application.Interfaces;

public interface IRecoveryService
{
    Task<PagedResult<RecoveryObligationDto>> GetRecoveryObligationsAsync(int page, int pageSize, string? status = null, Guid? ownerId = null, CancellationToken ct = default);
    Task<RecoverySummaryDto> GetRecoverySummaryAsync(CancellationToken ct = default);
    Task<RecoveryObligationDto> SettleObligationManuallyAsync(Guid obligationId, Guid adminId, ManualSettleRecoveryRequest request, CancellationToken ct = default);
    Task<RecoveryObligationDto> WriteOffObligationAsync(Guid obligationId, Guid adminId, string reason, CancellationToken ct = default);
}

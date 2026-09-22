using CourtBook.Application.Common;
using CourtBook.Application.DTOs;

namespace CourtBook.Application.Interfaces;

public interface IPayoutService
{
    // ── Owner Operations ────────────────────────────────────────────────────
    Task<OwnerBalanceDto> GetOwnerBalanceAsync(Guid ownerId, CancellationToken ct = default);
    Task<List<PayoutMethodDto>> GetPayoutMethodsAsync(Guid ownerId, CancellationToken ct = default);
    Task<PayoutMethodDto> CreatePayoutMethodAsync(Guid ownerId, CreatePayoutMethodRequest request, CancellationToken ct = default);
    Task<bool> DeletePayoutMethodAsync(Guid ownerId, Guid methodId, CancellationToken ct = default);
    Task<bool> SetDefaultPayoutMethodAsync(Guid ownerId, Guid methodId, CancellationToken ct = default);
    Task<PayoutRequestDto> RequestPayoutAsync(Guid ownerId, CreatePayoutRequest request, CancellationToken ct = default);
    Task<PagedResult<PayoutRequestDto>> GetOwnerPayoutsAsync(Guid ownerId, int page, int pageSize, string? status = null, CancellationToken ct = default);
    Task<PayoutRequestDto?> GetOwnerPayoutByIdAsync(Guid ownerId, Guid payoutId, CancellationToken ct = default);
    Task<bool> CancelPayoutRequestAsync(Guid ownerId, Guid payoutId, CancellationToken ct = default);

    // ── Admin Operations ────────────────────────────────────────────────────
    Task<PagedResult<PayoutRequestDto>> GetAdminPayoutsAsync(int page, int pageSize, string? status = null, Guid? ownerId = null, CancellationToken ct = default);
    Task<PayoutRequestDto?> GetAdminPayoutByIdAsync(Guid payoutId, CancellationToken ct = default);
    Task<PayoutRequestDto> ApprovePayoutAsync(Guid payoutId, Guid adminId, CancellationToken ct = default);
    Task<PayoutRequestDto> RejectPayoutAsync(Guid payoutId, Guid adminId, string reason, CancellationToken ct = default);
    Task<PayoutRequestDto> MarkPayoutPaidAsync(Guid payoutId, Guid adminId, MarkPayoutPaidRequest request, CancellationToken ct = default);
    Task<AdminPayoutSummaryDto> GetAdminPayoutSummaryAsync(CancellationToken ct = default);
}

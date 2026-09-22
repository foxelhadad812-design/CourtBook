using CourtBook.Application.Common;
using CourtBook.Application.DTOs;

namespace CourtBook.Application.Interfaces;

public interface ISettlementService
{
    Task<SettlementBatchDto> ExecuteSettlementBatchAsync(int bufferHours = 24, Guid? adminUserId = null, CancellationToken ct = default);
    Task<PagedResult<SettlementBatchDto>> GetSettlementBatchesAsync(int page, int pageSize, CancellationToken ct = default);
    Task<SettlementBatchDto?> GetSettlementBatchByIdAsync(Guid id, CancellationToken ct = default);
    Task<SettlementSummaryDto> GetSettlementSummaryAsync(CancellationToken ct = default);
}

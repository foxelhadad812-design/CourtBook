using CourtBook.Application.Common;
using CourtBook.Application.DTOs;

namespace CourtBook.Application.Interfaces;

public interface IAvailabilityService
{
    Task<Result<CourtAvailabilityResponse>> GetCourtAvailabilityAsync(Guid courtId, CourtAvailabilityRequest request);
}

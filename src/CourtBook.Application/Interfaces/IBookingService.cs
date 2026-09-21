using CourtBook.Application.Common;
using CourtBook.Application.DTOs;

namespace CourtBook.Application.Interfaces;

public interface IBookingService
{
    Task<BookingResponse> CreateAsync(Guid userId, CreateBookingRequest request);
    Task<List<BookingResponse>> GetMyBookingsAsync(Guid userId);
    Task<PagedResult<BookingResponse>> GetMyBookingsPagedAsync(Guid userId, BookingQueryRequest request);
    Task<BookingResponse?> GetByIdAsync(Guid userId, string userRole, Guid id);
    Task<CancellationPreviewResponse> GetCancellationPreviewAsync(Guid userId, string userRole, Guid id);
    Task<CancelBookingResult> CancelWithPolicyAsync(Guid userId, string userRole, Guid id, CancelBookingRequest? request);
    Task<bool> CancelAsync(Guid userId, string userRole, Guid id);
}

using CourtBook.Application.DTOs;

namespace CourtBook.Application.Interfaces;

public interface IBookingService
{
    Task<BookingResponse> CreateAsync(Guid userId, CreateBookingRequest request);
    Task<List<BookingResponse>> GetMyBookingsAsync(Guid userId);
    Task<BookingResponse?> GetByIdAsync(Guid userId, string userRole, Guid id);
    Task<bool> CancelAsync(Guid userId, string userRole, Guid id);
}

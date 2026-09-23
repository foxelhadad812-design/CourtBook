using CourtBook.Application.DTOs;

namespace CourtBook.Application.Interfaces;

public interface IPromoCodeService
{
    Task<ValidatePromoCodeResponse> ValidatePromoCodeAsync(ValidatePromoCodeRequest request, Guid? userId = null);
    Task<PromoCodeDto> CreatePromoCodeAsync(CreatePromoCodeRequest request);
    Task<List<PromoCodeDto>> GetAllPromoCodesAsync();
    Task<bool> DeactivatePromoCodeAsync(Guid id);
}

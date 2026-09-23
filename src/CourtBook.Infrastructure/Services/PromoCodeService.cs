using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Infrastructure.Services;

public class PromoCodeService : IPromoCodeService
{
    private readonly AppDbContext _dbContext;

    public PromoCodeService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ValidatePromoCodeResponse> ValidatePromoCodeAsync(ValidatePromoCodeRequest request, Guid? userId = null)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return new ValidatePromoCodeResponse
            {
                IsValid = false,
                ErrorMessage = "Please provide a valid promo code.",
                ErrorMessageAr = "يرجى إدخال كود خصم صحيح."
            };
        }

        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var promo = await _dbContext.PromoCodes
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == normalizedCode && p.IsActive);

        if (promo == null)
        {
            return new ValidatePromoCodeResponse
            {
                IsValid = false,
                ErrorMessage = "Promo code is invalid or does not exist.",
                ErrorMessageAr = "كود الخصم غير صالح أو غير موجود."
            };
        }

        var now = DateTime.UtcNow;

        if (promo.ValidFrom.HasValue && now < promo.ValidFrom.Value)
        {
            return new ValidatePromoCodeResponse
            {
                IsValid = false,
                ErrorMessage = "Promo code is not yet active.",
                ErrorMessageAr = "كود الخصم غير مفعّل بعد."
            };
        }

        if (promo.ValidTo.HasValue && now > promo.ValidTo.Value)
        {
            return new ValidatePromoCodeResponse
            {
                IsValid = false,
                ErrorMessage = "Promo code has expired.",
                ErrorMessageAr = "كود الخصم انتهت صلاحيته."
            };
        }

        if (promo.MaxUsages.HasValue && promo.UsageCount >= promo.MaxUsages.Value)
        {
            return new ValidatePromoCodeResponse
            {
                IsValid = false,
                ErrorMessage = "Promo code usage limit has been reached.",
                ErrorMessageAr = "تم استنفاد الحد الأقصى لاستخدام كود الخصم."
            };
        }

        if (promo.MinBookingAmount.HasValue && request.BookingAmount < promo.MinBookingAmount.Value)
        {
            return new ValidatePromoCodeResponse
            {
                IsValid = false,
                ErrorMessage = $"Minimum booking amount of {promo.MinBookingAmount.Value:N0} EGP is required.",
                ErrorMessageAr = $"الحد الأدنى لقيمة الحجز لتطبيق الكود هو {promo.MinBookingAmount.Value:N0} ج.م."
            };
        }

        if (userId.HasValue && userId.Value != Guid.Empty)
        {
            var alreadyUsed = await _dbContext.PromoCodeUsages
                .AsNoTracking()
                .AnyAsync(u => u.PromoCodeId == promo.Id && u.UserId == userId.Value);

            if (alreadyUsed)
            {
                return new ValidatePromoCodeResponse
                {
                    IsValid = false,
                    ErrorMessage = "You have already used this promo code.",
                    ErrorMessageAr = "لقد قمت باستخدام كود الخصم هذا مسبقاً."
                };
            }
        }

        decimal discount;
        if (promo.DiscountType == DiscountType.Percentage)
        {
            discount = (request.BookingAmount * promo.Value) / 100m;
            if (promo.MaxDiscountAmount.HasValue && discount > promo.MaxDiscountAmount.Value)
            {
                discount = promo.MaxDiscountAmount.Value;
            }
        }
        else
        {
            discount = promo.Value;
        }

        discount = Math.Min(discount, request.BookingAmount);
        var finalAmount = Math.Max(0m, request.BookingAmount - discount);

        return new ValidatePromoCodeResponse
        {
            IsValid = true,
            PromoCodeId = promo.Id,
            Code = promo.Code,
            DiscountType = promo.DiscountType,
            Value = promo.Value,
            DiscountAmount = Math.Round(discount, 2),
            FinalAmount = Math.Round(finalAmount, 2)
        };
    }

    public async Task<PromoCodeDto> CreatePromoCodeAsync(CreatePromoCodeRequest request)
    {
        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var exists = await _dbContext.PromoCodes.AnyAsync(p => p.Code == normalizedCode);
        if (exists)
        {
            throw new InvalidOperationException($"Promo code '{normalizedCode}' already exists.");
        }

        var promo = new PromoCode
        {
            Id = Guid.NewGuid(),
            Code = normalizedCode,
            Description = request.Description?.Trim(),
            DiscountType = request.DiscountType,
            Value = request.Value,
            MaxDiscountAmount = request.MaxDiscountAmount,
            MinBookingAmount = request.MinBookingAmount,
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
            MaxUsages = request.MaxUsages,
            UsageCount = 0,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.PromoCodes.Add(promo);
        await _dbContext.SaveChangesAsync();

        return MapToDto(promo);
    }

    public async Task<List<PromoCodeDto>> GetAllPromoCodesAsync()
    {
        var promos = await _dbContext.PromoCodes
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        return promos.Select(MapToDto).ToList();
    }

    public async Task<bool> DeactivatePromoCodeAsync(Guid id)
    {
        var promo = await _dbContext.PromoCodes.FindAsync(id);
        if (promo == null) return false;

        promo.IsActive = false;
        await _dbContext.SaveChangesAsync();
        return true;
    }

    private static PromoCodeDto MapToDto(PromoCode p) => new()
    {
        Id = p.Id,
        Code = p.Code,
        Description = p.Description,
        DiscountType = p.DiscountType,
        Value = p.Value,
        MaxDiscountAmount = p.MaxDiscountAmount,
        MinBookingAmount = p.MinBookingAmount,
        ValidFrom = p.ValidFrom,
        ValidTo = p.ValidTo,
        MaxUsages = p.MaxUsages,
        UsageCount = p.UsageCount,
        IsActive = p.IsActive,
        CreatedAt = p.CreatedAt
    };
}

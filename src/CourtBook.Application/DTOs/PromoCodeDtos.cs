using CourtBook.Domain.Enums;

namespace CourtBook.Application.DTOs;

public class PromoCodeDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DiscountType DiscountType { get; set; }
    public decimal Value { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public decimal? MinBookingAmount { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public int? MaxUsages { get; set; }
    public int UsageCount { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreatePromoCodeRequest
{
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DiscountType DiscountType { get; set; } = DiscountType.Percentage;
    public decimal Value { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public decimal? MinBookingAmount { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public int? MaxUsages { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ValidatePromoCodeRequest
{
    public string Code { get; set; } = string.Empty;
    public decimal BookingAmount { get; set; }
}

public class ValidatePromoCodeResponse
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorMessageAr { get; set; }
    public Guid? PromoCodeId { get; set; }
    public string Code { get; set; } = string.Empty;
    public DiscountType DiscountType { get; set; }
    public decimal Value { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FinalAmount { get; set; }
}

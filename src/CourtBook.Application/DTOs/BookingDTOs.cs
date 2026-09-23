using CourtBook.Application.Common;

namespace CourtBook.Application.DTOs;

public class CreateBookingRequest
{
    public Guid CourtId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? Notes { get; set; }
    public string? PromoCode { get; set; }
    public List<SelectedAddonDto>? Addons { get; set; }
}

public class BookingQueryRequest : PagedRequest
{
    public string? Status { get; set; } // "upcoming", "completed", "cancelled", "all"
    public string? Sport { get; set; }
}

public class BookingResponse
{
    public Guid Id { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public Guid CourtId { get; set; }
    public string CourtName { get; set; } = string.Empty;
    public string SportType { get; set; } = string.Empty;
    public Guid VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string VenueCity { get; set; } = string.Empty;
    public string VenueAddress { get; set; } = string.Empty;
    public string VenuePhone { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal TotalPrice { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }

    // Phase 12: Manual booking & Check-in
    public bool IsManualBooking { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public bool IsCheckedIn { get; set; }
    public DateTime? CheckedInAt { get; set; }

    // Phase 12: Promo code & Addons
    public decimal DiscountAmount { get; set; }
    public string? PromoCode { get; set; }
    public List<BookingAddonDto> Addons { get; set; } = [];
    public string? GoogleMapsUrl { get; set; }

    // Cancellation info
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public bool CanCancel { get; set; }
    public int FreeCancellationHours { get; set; }
    public decimal LateCancellationFeePercent { get; set; }
    public string? CancellationPolicyDescription { get; set; }

    // Review info
    public bool IsEligibleForReview { get; set; }
    public bool HasReviewed { get; set; }
    public Guid? ReviewId { get; set; }
}

public class CancellationPreviewResponse
{
    public Guid BookingId { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public string VenueName { get; set; } = string.Empty;
    public string CourtName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public double HoursUntilStart { get; set; }
    public bool CanCancel { get; set; }
    public string? ReasonIfNotAllowed { get; set; }
    public bool IsFreeCancellation { get; set; }
    public int FreeCancellationHours { get; set; }
    public decimal LateCancellationFeePercent { get; set; }
    public decimal TotalPrice { get; set; }
    public decimal CancellationFee { get; set; }
    public decimal RefundAmount { get; set; }
    public string PolicyDescription { get; set; } = string.Empty;
}

public class CancelBookingRequest
{
    public string? Reason { get; set; }
}

public class CancelBookingResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public decimal CancellationFee { get; set; }
    public decimal RefundAmount { get; set; }
    public DateTime CancelledAt { get; set; }
}

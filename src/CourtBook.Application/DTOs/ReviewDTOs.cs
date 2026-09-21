using CourtBook.Application.Common;

namespace CourtBook.Application.DTOs;

public class CreateReviewRequest
{
    public Guid BookingId { get; set; }
    public int OverallRating { get; set; }
    public int CourtQualityRating { get; set; }
    public int CleanlinessRating { get; set; }
    public int StaffRating { get; set; }
    public int ValueRating { get; set; }
    public string? Comment { get; set; }
}

public class OwnerResponseRequest
{
    public string Response { get; set; } = string.Empty;
}

public class VenueRatingSummaryDto
{
    public Guid VenueId { get; set; }
    public double AverageRating { get; set; }
    public int TotalReviews { get; set; }
    public int FiveStarCount { get; set; }
    public int FourStarCount { get; set; }
    public int ThreeStarCount { get; set; }
    public int TwoStarCount { get; set; }
    public int OneStarCount { get; set; }
    public double FiveStarPercent { get; set; }
    public double FourStarPercent { get; set; }
    public double ThreeStarPercent { get; set; }
    public double TwoStarPercent { get; set; }
    public double OneStarPercent { get; set; }
    public double CourtQualityAverage { get; set; }
    public double CleanlinessAverage { get; set; }
    public double StaffAverage { get; set; }
    public double ValueAverage { get; set; }
}

public class ReviewResponse
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public Guid VenueId { get; set; }
    public string? CourtName { get; set; }
    public bool IsVerifiedBooking { get; set; } = true;
    public int OverallRating { get; set; }
    public int CourtQualityRating { get; set; }
    public int CleanlinessRating { get; set; }
    public int StaffRating { get; set; }
    public int ValueRating { get; set; }
    public string? Comment { get; set; }
    public string? OwnerResponse { get; set; }
    public DateTime? OwnerRespondedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

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

public class ReviewResponse
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public Guid VenueId { get; set; }
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

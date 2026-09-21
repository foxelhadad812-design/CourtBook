using CourtBook.Application.Common;

namespace CourtBook.Application.DTOs;

public class OwnerDashboardSummaryDto
{
    public int TotalVenues { get; set; }
    public int TotalCourts { get; set; }
    public int UpcomingBookingsCount { get; set; }
    public int CompletedBookingsCount { get; set; }
    public int CancelledBookingsCount { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal RevenueThisMonth { get; set; }
    public decimal RevenueThisWeek { get; set; }
    public int BookingsToday { get; set; }
    public int BookingsThisWeek { get; set; }
    public string? MostBookedCourtName { get; set; }
    public int MostBookedCourtCount { get; set; }

    public List<OwnerVenueDto> Venues { get; set; } = [];
    public List<OwnerBookingDto> RecentBookings { get; set; } = [];
    public OwnerAnalyticsDto Analytics { get; set; } = new();
}

public class OwnerVenueDto
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Country { get; set; } = "Egypt";
    public List<string> Sports { get; set; } = [];
    public int TotalCourts { get; set; }
    public double AverageRating { get; set; }
    public int TotalReviews { get; set; }
    public bool IsActive { get; set; }
    public bool IsVerified { get; set; }
    public int UpcomingBookingsCount { get; set; }
    public string? PrimaryImageUrl { get; set; }
}

public class OwnerBookingDto
{
    public Guid Id { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public Guid VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public Guid CourtId { get; set; }
    public string CourtName { get; set; } = string.Empty;
    public string SportType { get; set; } = string.Empty;

    // Customer / Player Info
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string PlayerEmail { get; set; } = string.Empty;
    public string PlayerPhone { get; set; } = string.Empty;

    // Schedule & Status
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int DurationMinutes { get; set; }
    public decimal TotalPrice { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string BookingStatus { get; set; } = string.Empty;
    public string? CancellationReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class OwnerBookingQueryRequest : PagedRequest
{
    public Guid? VenueId { get; set; }
    public string? Status { get; set; } // "upcoming", "completed", "cancelled", "all"
    public string? Sport { get; set; }
    public DateOnly? Date { get; set; }
}

public class OwnerAnalyticsDto
{
    public int BookingsToday { get; set; }
    public int BookingsThisWeek { get; set; }
    public int CompletedBookings { get; set; }
    public int CancelledBookings { get; set; }
    public decimal RevenueToday { get; set; }
    public decimal RevenueThisWeek { get; set; }
    public decimal RevenueThisMonth { get; set; }
    public decimal TotalRevenue { get; set; }
    public string? MostBookedCourtName { get; set; }
    public int MostBookedCourtCount { get; set; }
    public string? TopSport { get; set; }
}

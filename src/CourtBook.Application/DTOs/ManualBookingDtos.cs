namespace CourtBook.Application.DTOs;

public class CreateManualBookingRequest
{
    public Guid CourtId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public decimal? CustomPrice { get; set; }
    public string? Notes { get; set; }
    public List<SelectedAddonDto>? Addons { get; set; }
}

public class QuickCheckInRequest
{
    public string BookingReference { get; set; } = string.Empty;
}

public class QuickCheckInResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string MessageAr { get; set; } = string.Empty;
    public BookingResponse? Booking { get; set; }
}

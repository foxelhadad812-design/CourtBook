namespace CourtBook.Application.DTOs;

public class CreateScheduleRequest
{
    public int DayOfWeek { get; set; } // 0-6
    public string OpenTime { get; set; } = string.Empty; // "HH:mm"
    public string CloseTime { get; set; } = string.Empty; // "HH:mm"
}

public class ScheduleResponse
{
    public Guid Id { get; set; }
    public Guid CourtId { get; set; }
    public int DayOfWeek { get; set; }
    public string OpenTime { get; set; } = string.Empty;
    public string CloseTime { get; set; } = string.Empty;
}

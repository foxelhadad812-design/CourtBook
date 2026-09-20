namespace CourtBook.Web.Models;

public class LoginRequest { public string Email { get; set; } = ""; public string Password { get; set; } = ""; }
public class AuthResponse { public string Token { get; set; } = ""; public string Role { get; set; } = ""; }
public class RegisterRequest { public string Name { get; set; } = ""; public string Email { get; set; } = ""; public string Password { get; set; } = ""; public string Phone { get; set; } = ""; }

public class VenueResponse { public Guid Id { get; set; } public Guid OwnerId { get; set; } public string Name { get; set; } = ""; public string City { get; set; } = ""; public string Address { get; set; } = ""; public List<CourtResponse> Courts { get; set; } = []; }
public class CourtResponse { public Guid Id { get; set; } public string Name { get; set; } = ""; public string SportType { get; set; } = ""; public decimal PricePerHour { get; set; } public bool IsActive { get; set; } public List<ScheduleResponse> Schedules { get; set; } = []; }
public class ScheduleResponse { public Guid Id { get; set; } public int DayOfWeek { get; set; } public string OpenTime { get; set; } = ""; public string CloseTime { get; set; } = ""; }

public class CreateVenueRequest { public string Name { get; set; } = ""; public string City { get; set; } = ""; public string Address { get; set; } = ""; }
public class CreateCourtRequest { public string Name { get; set; } = ""; public string SportType { get; set; } = ""; public decimal PricePerHour { get; set; } public bool IsActive { get; set; } }
public class CreateScheduleRequest { public int DayOfWeek { get; set; } public string OpenTime { get; set; } = ""; public string CloseTime { get; set; } = ""; }

public class BookingResponse { public Guid Id { get; set; } public Guid CourtId { get; set; } public DateTime StartTime { get; set; } public DateTime EndTime { get; set; } public string Status { get; set; } = ""; public decimal TotalPrice { get; set; } public DateTime CreatedAt { get; set; } }
public class CreateBookingRequest { public Guid CourtId { get; set; } public DateTime StartTime { get; set; } public DateTime EndTime { get; set; } }

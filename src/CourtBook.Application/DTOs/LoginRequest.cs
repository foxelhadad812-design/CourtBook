namespace CourtBook.Application.DTOs;

/// <summary>Credentials submitted on the login form.</summary>
public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    // Optional device & client metadata for mobile session tracking
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string? Platform { get; set; }
    public string? AppVersion { get; set; }
}

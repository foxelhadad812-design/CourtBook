namespace CourtBook.Application.DTOs;

/// <summary>Payload sent by a new user wanting to create an account.</summary>
public class RegisterRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Role { get; set; } = "Client"; // "Client" or "Owner"
    public DateOnly? DateOfBirth { get; set; }
    public bool AcceptTerms { get; set; }
    public string? TermsVersion { get; set; }

    // Optional device & client metadata for mobile session tracking
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string? Platform { get; set; }
    public string? AppVersion { get; set; }
}

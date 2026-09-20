namespace CourtBook.Application.DTOs;

/// <summary>Credentials submitted on the login form.</summary>
public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

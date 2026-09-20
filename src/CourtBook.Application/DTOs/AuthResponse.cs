namespace CourtBook.Application.DTOs;

/// <summary>Returned to the client after a successful register or login.</summary>
public class AuthResponse
{
    public string Token { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

using CourtBook.Application.DTOs;

namespace CourtBook.Application.Interfaces;

/// <summary>
/// Handles user registration and login.
/// Implemented in the Infrastructure layer.
/// </summary>
public interface IAuthService
{
    /// <summary>Creates a new Client account. Throws if email already exists.</summary>
    Task<AuthResponse> RegisterAsync(RegisterRequest request);

    /// <summary>Validates credentials and returns a signed JWT. Returns null on failure.</summary>
    Task<AuthResponse?> LoginAsync(LoginRequest request);
}

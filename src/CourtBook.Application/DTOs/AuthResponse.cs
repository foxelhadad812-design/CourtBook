namespace CourtBook.Application.DTOs;

/// <summary>
/// Returned to the client after a successful register, login, or token refresh.
/// Fully backwards-compatible with Web client while providing mobile-ready session tokens.
/// </summary>
public class AuthResponse
{
    /// <summary>Legacy access token property for backward compatibility with Web client.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Short-lived JWT access token for authenticating API requests.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the access token expires.</summary>
    public DateTime AccessTokenExpiresAt { get; set; }

    /// <summary>Long-lived cryptographically secure refresh token for session renewal.</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the refresh token expires.</summary>
    public DateTime RefreshTokenExpiresAt { get; set; }

    /// <summary>Token scheme type, default is Bearer.</summary>
    public string TokenType { get; set; } = "Bearer";

    /// <summary>Authenticated user unique identifier.</summary>
    public Guid UserId { get; set; }

    /// <summary>Authenticated user email address.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Authenticated user full name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Authenticated user role (e.g. Client, Owner, Admin).</summary>
    public string Role { get; set; } = string.Empty;
}

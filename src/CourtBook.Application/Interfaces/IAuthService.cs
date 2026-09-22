using CourtBook.Application.DTOs;

namespace CourtBook.Application.Interfaces;

/// <summary>
/// Handles user registration, authentication, token rotation, and session management.
/// Implemented in the Infrastructure layer.
/// </summary>
public interface IAuthService
{
    /// <summary>Creates a new account (Client or Owner). Throws if email already exists or validation fails.</summary>
    Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ipAddress = null);

    /// <summary>Validates credentials and returns access and refresh tokens. Returns null on failure.</summary>
    Task<AuthResponse?> LoginAsync(LoginRequest request, string? ipAddress = null);

    /// <summary>Rotates the single-use refresh token and returns a new access + refresh token pair.</summary>
    Task<AuthResponse?> RefreshTokenAsync(RefreshTokenRequest request, string? ipAddress = null);

    /// <summary>Revokes a single refresh token (e.g. client logout).</summary>
    Task<bool> RevokeTokenAsync(string rawRefreshToken, string? ipAddress = null, string? reason = null, Guid? authenticatedUserId = null);

    /// <summary>Revokes all active refresh tokens for a user (e.g. logout from all devices).</summary>
    Task<int> RevokeAllUserTokensAsync(Guid userId, string? ipAddress = null, string? reason = null);

    /// <summary>Retrieves active and recent device sessions for a user.</summary>
    Task<List<DeviceSessionDto>> GetUserSessionsAsync(Guid userId, string? currentRawToken = null);

    /// <summary>Revokes a specific device session belonging to the user.</summary>
    Task<bool> RevokeSessionAsync(Guid userId, Guid sessionId, string? ipAddress = null);
}

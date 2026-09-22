using CourtBook.API.Extensions;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

namespace CourtBook.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>Registers a new Client or Owner account and returns an access token + refresh token session.</summary>
    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name, Email, and Password are required.");
        }

        try
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var response = await _authService.RegisterAsync(request, ip);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    /// <summary>Authenticates a user and returns an access token + refresh token session on success.</summary>
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest("Email and Password are required.");
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var response = await _authService.LoginAsync(request, ip);

        if (response is null)
            return Unauthorized("Invalid email or password.");

        return Ok(response);
    }

    /// <summary>Rotates a single-use refresh token and returns a new access + refresh token pair.</summary>
    [HttpPost("refresh")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return BadRequest("Refresh token is required.");

        try
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var response = await _authService.RefreshTokenAsync(request, ip);

            if (response is null)
                return Unauthorized(new { message = "Invalid refresh token." });

            return Ok(response);
        }
        catch (SecurityTokenExpiredException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (SecurityTokenException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    /// <summary>Revokes a refresh token (e.g. mobile client logout).</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Logout([FromBody] RevokeTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return BadRequest("Refresh token is required.");

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await _authService.RevokeTokenAsync(request.RefreshToken, ip, "User logged out");

        return Ok(new { message = "Successfully logged out." });
    }

    /// <summary>Terminates all active sessions for the authenticated user across all devices.</summary>
    [Authorize]
    [HttpPost("logout-all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LogoutAll()
    {
        var userId = User.GetUserId();
        if (userId == Guid.Empty)
            return Unauthorized();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var count = await _authService.RevokeAllUserTokensAsync(userId, ip, "User logged out from all devices");

        return Ok(new { message = "All active sessions have been terminated.", terminatedCount = count });
    }

    /// <summary>Lists all active device sessions for the authenticated user.</summary>
    [Authorize]
    [HttpGet("sessions")]
    [ProducesResponseType(typeof(List<DeviceSessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetSessions()
    {
        var userId = User.GetUserId();
        if (userId == Guid.Empty)
            return Unauthorized();

        var sessions = await _authService.GetUserSessionsAsync(userId);
        return Ok(sessions);
    }

    /// <summary>Revokes a specific device session belonging to the authenticated user.</summary>
    [Authorize]
    [HttpDelete("sessions/{sessionId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeSession(Guid sessionId)
    {
        var userId = User.GetUserId();
        if (userId == Guid.Empty)
            return Unauthorized();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var success = await _authService.RevokeSessionAsync(userId, sessionId, ip);

        if (!success)
            return NotFound(new { message = "Session not found." });

        return Ok(new { message = "Session revoked successfully." });
    }
}

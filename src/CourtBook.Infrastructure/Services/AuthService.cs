using System.Security.Cryptography;
using System.Text;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace CourtBook.Infrastructure.Services;

/// <summary>
/// Handles user registration, login, token rotation, reuse detection, and session revocation.
/// Uses BCrypt for password hashing and ITokenService to issue signed JWTs.
/// Stores only SHA-256 hashes of refresh tokens, tracking token families for zero-trust theft detection.
/// </summary>
public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly ITokenService _tokenService;

    public AuthService(AppDbContext db, ITokenService tokenService)
    {
        _db = db;
        _tokenService = tokenService;
    }

    /// <summary>
    /// Registers a new user with the selected role (Client or Owner), records terms acceptance,
    /// and issues an access token + initial refresh token session.
    /// </summary>
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ipAddress = null)
    {
        // Guard: terms acceptance
        if (!request.AcceptTerms)
            throw new InvalidOperationException("You must accept the terms of service to register.");

        // Guard: duplicate email
        bool emailExists = await _db.Users
            .AnyAsync(u => u.Email == request.Email);

        if (emailExists)
            throw new InvalidOperationException("Email is already registered.");

        // Determine role: Client (default) or Owner. Admin registration via public endpoint is prohibited.
        var assignedRole = Role.Client;
        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            if (string.Equals(request.Role, "Owner", StringComparison.OrdinalIgnoreCase))
            {
                assignedRole = Role.Owner;
            }
            else if (string.Equals(request.Role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Registration as an Administrator is not allowed.");
            }
        }

        if (request.DateOfBirth.HasValue)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (request.DateOfBirth.Value > today)
                throw new InvalidOperationException("Date of birth cannot be in the future.");

            var age = today.Year - request.DateOfBirth.Value.Year;
            if (request.DateOfBirth.Value > today.AddYears(-age)) age--;
            if (age < 6)
                throw new InvalidOperationException("You must be at least 6 years old to register.");
        }

        var user = new User
        {
            Id           = Guid.NewGuid(),
            Name         = request.Name,
            Email        = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Phone        = request.Phone,
            Role         = assignedRole,
            DateOfBirth  = request.DateOfBirth
        };

        _db.Users.Add(user);

        // Record terms acceptance audit
        var termsType = assignedRole == Role.Owner ? TermsType.FacilityOwner : TermsType.Player;
        var termsDoc = await _db.TermsDocuments
            .FirstOrDefaultAsync(t => t.Type == termsType && t.IsActive);

        if (termsDoc != null)
        {
            _db.TermsAcceptances.Add(new TermsAcceptance
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TermsDocumentId = termsDoc.Id,
                AcceptedAt = DateTime.UtcNow
            });
        }

        // Generate initial refresh token session
        var rawRefreshToken = GenerateSecureRefreshToken();
        var tokenHash = HashToken(rawRefreshToken);
        var refreshExpiresAt = DateTime.UtcNow.AddDays(30);
        var familyId = Guid.NewGuid();

        var refreshTokenEntity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenHash,
            FamilyId = familyId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = refreshExpiresAt,
            DeviceId = request.DeviceId,
            DeviceName = request.DeviceName,
            Platform = request.Platform,
            AppVersion = request.AppVersion,
            CreatedByIp = ipAddress
        };

        _db.RefreshTokens.Add(refreshTokenEntity);
        await _db.SaveChangesAsync();

        var (accessToken, accessExpiresAt) = _tokenService.GenerateAccessToken(user);

        return new AuthResponse
        {
            Token                 = accessToken,
            AccessToken           = accessToken,
            AccessTokenExpiresAt  = accessExpiresAt,
            RefreshToken          = rawRefreshToken,
            RefreshTokenExpiresAt = refreshExpiresAt,
            UserId                = user.Id,
            Email                 = user.Email,
            Name                  = user.Name,
            Role                  = user.Role.ToString(),
            TokenType             = "Bearer"
        };
    }

    /// <summary>
    /// Validates credentials and returns an access token + initial refresh token session.
    /// Returns null if credentials do not match or user is inactive.
    /// </summary>
    public async Task<AuthResponse?> LoginAsync(LoginRequest request, string? ipAddress = null)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == request.Email);

        // Unknown email, wrong password, or inactive user
        if (user is null || !user.IsActive || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return null;

        var (accessToken, accessExpiresAt) = _tokenService.GenerateAccessToken(user);

        // Generate refresh token session
        var rawRefreshToken = GenerateSecureRefreshToken();
        var tokenHash = HashToken(rawRefreshToken);
        var refreshExpiresAt = DateTime.UtcNow.AddDays(30);
        var familyId = Guid.NewGuid();

        var refreshTokenEntity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenHash,
            FamilyId = familyId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = refreshExpiresAt,
            DeviceId = request.DeviceId,
            DeviceName = request.DeviceName,
            Platform = request.Platform,
            AppVersion = request.AppVersion,
            CreatedByIp = ipAddress
        };

        _db.RefreshTokens.Add(refreshTokenEntity);
        await _db.SaveChangesAsync();

        return new AuthResponse
        {
            Token                 = accessToken,
            AccessToken           = accessToken,
            AccessTokenExpiresAt  = accessExpiresAt,
            RefreshToken          = rawRefreshToken,
            RefreshTokenExpiresAt = refreshExpiresAt,
            UserId                = user.Id,
            Email                 = user.Email,
            Name                  = user.Name,
            Role                  = user.Role.ToString(),
            TokenType             = "Bearer"
        };
    }

    /// <summary>
    /// Rotates the single-use refresh token and returns a new access + refresh token pair.
    /// Neutralizes theft attacks: if a revoked token is reused, all tokens in the family are revoked immediately.
    /// </summary>
    public async Task<AuthResponse?> RefreshTokenAsync(RefreshTokenRequest request, string? ipAddress = null)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return null;

        var tokenHash = HashToken(request.RefreshToken);

        var token = await _db.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash);

        if (token is null)
            return null;

        // REUSE DETECTION: If an already-revoked refresh token is presented, trigger family revocation!
        if (token.IsRevoked)
        {
            var compromisedFamilyTokens = await _db.RefreshTokens
                .Where(r => r.FamilyId == token.FamilyId && r.RevokedAt == null)
                .ToListAsync();

            foreach (var famToken in compromisedFamilyTokens)
            {
                famToken.RevokedAt = DateTime.UtcNow;
                famToken.ReasonRevoked = "Revoked due to detected token reuse attack";
            }

            await _db.SaveChangesAsync();

            throw new SecurityTokenException("Compromised refresh token reuse detected. All sessions in this token family have been terminated.");
        }

        // Expired token check
        if (token.IsExpired)
        {
            throw new SecurityTokenExpiredException("Refresh token has expired. Please log in again.");
        }

        // Inactive user check
        if (!token.User.IsActive)
        {
            throw new SecurityTokenException("User account is inactive.");
        }

        // Legitimate rotation: mark current token as rotated
        token.RevokedAt = DateTime.UtcNow;
        token.ReasonRevoked = "Rotated";

        var rawNewRefreshToken = GenerateSecureRefreshToken();
        var newHash = HashToken(rawNewRefreshToken);
        var refreshExpiresAt = DateTime.UtcNow.AddDays(30);

        var newToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = token.UserId,
            TokenHash = newHash,
            FamilyId = token.FamilyId, // Preserve family chain
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = refreshExpiresAt,
            DeviceId = request.DeviceId ?? token.DeviceId,
            DeviceName = request.DeviceName ?? token.DeviceName,
            Platform = request.Platform ?? token.Platform,
            AppVersion = request.AppVersion ?? token.AppVersion,
            CreatedByIp = ipAddress
        };

        token.ReplacedByTokenId = newToken.Id;

        _db.RefreshTokens.Add(newToken);
        await _db.SaveChangesAsync();

        var (accessToken, accessExpiresAt) = _tokenService.GenerateAccessToken(token.User);

        return new AuthResponse
        {
            Token                 = accessToken,
            AccessToken           = accessToken,
            AccessTokenExpiresAt  = accessExpiresAt,
            RefreshToken          = rawNewRefreshToken,
            RefreshTokenExpiresAt = refreshExpiresAt,
            UserId                = token.UserId,
            Email                 = token.User.Email,
            Name                  = token.User.Name,
            Role                  = token.User.Role.ToString(),
            TokenType             = "Bearer"
        };
    }

    /// <summary>
    /// Revokes a single refresh token (e.g. client logout).
    /// </summary>
    public async Task<bool> RevokeTokenAsync(string rawRefreshToken, string? ipAddress = null, string? reason = null)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
            return false;

        var tokenHash = HashToken(rawRefreshToken);

        var token = await _db.RefreshTokens
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash);

        if (token is null || token.IsRevoked)
            return false;

        token.RevokedAt = DateTime.UtcNow;
        token.ReasonRevoked = reason ?? "Revoked by user logout";

        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Revokes all active refresh tokens for a user (e.g. logout from all devices).
    /// </summary>
    public async Task<int> RevokeAllUserTokensAsync(Guid userId, string? ipAddress = null, string? reason = null)
    {
        var activeTokens = await _db.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAt == null && r.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();

        if (activeTokens.Count == 0)
            return 0;

        foreach (var t in activeTokens)
        {
            t.RevokedAt = DateTime.UtcNow;
            t.ReasonRevoked = reason ?? "Logged out from all devices";
        }

        await _db.SaveChangesAsync();
        return activeTokens.Count;
    }

    /// <summary>
    /// Retrieves active device sessions for a user.
    /// </summary>
    public async Task<List<DeviceSessionDto>> GetUserSessionsAsync(Guid userId, string? currentRawToken = null)
    {
        string? currentHash = !string.IsNullOrWhiteSpace(currentRawToken) ? HashToken(currentRawToken) : null;

        var sessions = await _db.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAt == null && r.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new DeviceSessionDto
            {
                SessionId        = r.Id,
                FamilyId         = r.FamilyId,
                DeviceId         = r.DeviceId,
                DeviceName       = r.DeviceName,
                Platform         = r.Platform,
                AppVersion       = r.AppVersion,
                IpAddress        = r.CreatedByIp,
                CreatedAt        = r.CreatedAt,
                ExpiresAt        = r.ExpiresAt,
                IsActive         = true,
                IsCurrentSession = currentHash != null && r.TokenHash == currentHash
            })
            .ToListAsync();

        return sessions;
    }

    /// <summary>
    /// Revokes a specific device session belonging to the user.
    /// </summary>
    public async Task<bool> RevokeSessionAsync(Guid userId, Guid sessionId, string? ipAddress = null)
    {
        var token = await _db.RefreshTokens
            .FirstOrDefaultAsync(r => r.Id == sessionId && r.UserId == userId);

        if (token is null)
            return false;

        // Revoke all tokens in this family to ensure the entire device chain is terminated
        var familyTokens = await _db.RefreshTokens
            .Where(r => r.FamilyId == token.FamilyId && r.RevokedAt == null)
            .ToListAsync();

        foreach (var t in familyTokens)
        {
            t.RevokedAt = DateTime.UtcNow;
            t.ReasonRevoked = "Session revoked by user";
        }

        await _db.SaveChangesAsync();
        return true;
    }

    // ── Helper Utilities ─────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a cryptographically secure 512-bit random token formatted as URL-safe Base64.
    /// </summary>
    public static string GenerateSecureRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Computes the SHA-256 hash of a raw token formatted as a lowercase hex string.
    /// </summary>
    public static string HashToken(string rawToken)
    {
        var bytes = Encoding.UTF8.GetBytes(rawToken);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

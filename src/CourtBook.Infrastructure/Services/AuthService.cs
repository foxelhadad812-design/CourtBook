using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Infrastructure.Services;

/// <summary>
/// Handles user registration and login.
/// Uses BCrypt for password hashing and ITokenService to issue JWTs.
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
    /// Registers a new user with the Client role.
    /// Throws InvalidOperationException if the email is already taken.
    /// </summary>
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        // Guard: duplicate email
        bool emailExists = await _db.Users
            .AnyAsync(u => u.Email == request.Email);

        if (emailExists)
            throw new InvalidOperationException("Email is already registered.");

        var user = new User
        {
            Id           = Guid.NewGuid(),
            Name         = request.Name,
            Email        = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Phone        = request.Phone,
            Role         = Role.Client   // New registrations are always Clients
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return new AuthResponse
        {
            Token = _tokenService.GenerateToken(user),
            Name  = user.Name,
            Role  = user.Role.ToString()
        };
    }

    /// <summary>
    /// Validates credentials. Returns null if the email doesn't exist or the
    /// password doesn't match — the controller translates this to a 401.
    /// </summary>
    public async Task<AuthResponse?> LoginAsync(LoginRequest request)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == request.Email);

        // Unknown email or wrong password
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return null;

        return new AuthResponse
        {
            Token = _tokenService.GenerateToken(user),
            Name  = user.Name,
            Role  = user.Role.ToString()
        };
    }
}

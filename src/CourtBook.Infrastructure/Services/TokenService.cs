using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace CourtBook.Infrastructure.Services;

/// <summary>
/// Builds a signed JWT containing the user's identity claims.
/// Key, issuer, and audience are read from JwtSettings in appsettings.json.
/// Supports both short-lived access tokens (ExpiryInMinutes) and legacy day-based tokens (ExpiryInDays).
/// </summary>
public class TokenService : ITokenService
{
    private readonly IConfiguration _config;

    public TokenService(IConfiguration config)
    {
        _config = config;
    }

    public (string Token, DateTime ExpiresAt) GenerateAccessToken(User user)
    {
        var jwtSettings = _config.GetSection("JwtSettings");

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtSettings["Key"]!));

        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Claims embedded in the token payload
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub,   user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Name,  user.Name),
            new Claim(ClaimTypes.Role,               user.Role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString())
        };

        DateTime expiresAt;
        if (int.TryParse(jwtSettings["ExpiryInMinutes"], out var minutes) && minutes > 0)
        {
            expiresAt = DateTime.UtcNow.AddMinutes(minutes);
        }
        else if (int.TryParse(jwtSettings["ExpiryInDays"], out var days) && days > 0)
        {
            expiresAt = DateTime.UtcNow.AddDays(days);
        }
        else
        {
            expiresAt = DateTime.UtcNow.AddMinutes(60);
        }

        var token = new JwtSecurityToken(
            issuer:             jwtSettings["Issuer"],
            audience:           jwtSettings["Audience"],
            claims:             claims,
            expires:            expiresAt,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public string GenerateToken(User user)
    {
        return GenerateAccessToken(user).Token;
    }
}

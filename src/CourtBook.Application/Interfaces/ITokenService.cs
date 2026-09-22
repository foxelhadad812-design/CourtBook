using CourtBook.Domain.Entities;

namespace CourtBook.Application.Interfaces;

/// <summary>
/// Generates signed JWT access tokens.
/// Implemented in the Infrastructure layer.
/// </summary>
public interface ITokenService
{
    /// <summary>Builds and signs a JWT containing the user's Id, Email, Name, and Role claims.</summary>
    string GenerateToken(User user);

    /// <summary>Builds and signs a JWT and returns its expiration time in UTC.</summary>
    (string Token, DateTime ExpiresAt) GenerateAccessToken(User user) => (GenerateToken(user), DateTime.UtcNow.AddMinutes(60));
}

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
}

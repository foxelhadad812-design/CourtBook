namespace CourtBook.Application.DTOs;

/// <summary>Request payload for revoking a refresh token (e.g. logging out from a mobile client).</summary>
public class RevokeTokenRequest
{
    /// <summary>The raw refresh token to revoke.</summary>
    public string RefreshToken { get; set; } = string.Empty;
}

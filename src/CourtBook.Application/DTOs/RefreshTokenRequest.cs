namespace CourtBook.Application.DTOs;

/// <summary>Request payload for rotating a refresh token.</summary>
public class RefreshTokenRequest
{
    /// <summary>The raw, single-use refresh token previously issued to the client.</summary>
    public string RefreshToken { get; set; } = string.Empty;

    // Optional updated device metadata
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string? Platform { get; set; }
    public string? AppVersion { get; set; }
}

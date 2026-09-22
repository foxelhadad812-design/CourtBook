namespace CourtBook.Application.DTOs;

/// <summary>Represents an active device session for a user.</summary>
public class DeviceSessionDto
{
    public Guid SessionId { get; set; }
    public Guid FamilyId { get; set; }
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string? Platform { get; set; }
    public string? AppVersion { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public bool IsCurrentSession { get; set; }
}

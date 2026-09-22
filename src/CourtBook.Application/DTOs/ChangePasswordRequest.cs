namespace CourtBook.Application.DTOs;

/// <summary>
/// Request model for changing an authenticated user's password.
/// </summary>
public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

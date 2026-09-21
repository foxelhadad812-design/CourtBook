namespace CourtBook.Domain.Entities;

/// <summary>
/// Audit record of an explicit terms acceptance by a user.
/// </summary>
public class TermsAcceptance
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TermsDocumentId { get; set; }
    public DateTime AcceptedAt { get; set; } = DateTime.UtcNow;
    public string? IpAddress { get; set; }

    // Navigation properties
    public User User { get; set; } = null!;
    public TermsDocument TermsDocument { get; set; } = null!;
}

using CourtBook.Domain.Enums;

namespace CourtBook.Domain.Entities;

/// <summary>
/// A legally binding terms document (e.g., Player Terms of Use, Facility Owner Requirements).
/// </summary>
public class TermsDocument
{
    public Guid Id { get; set; }
    public TermsType Type { get; set; }
    public string Version { get; set; } = "1.0";
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public ICollection<TermsAcceptance> Acceptances { get; set; } = [];
}

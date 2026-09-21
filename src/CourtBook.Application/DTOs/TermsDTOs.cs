using CourtBook.Domain.Enums;

namespace CourtBook.Application.DTOs;

public class TermsDocumentDto
{
    public Guid Id { get; set; }
    public TermsType Type { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? TitleAr { get; set; }
    public string? ContentAr { get; set; }
    public DateTime PublishedAt { get; set; }
}

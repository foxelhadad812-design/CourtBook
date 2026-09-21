using CourtBook.Application.DTOs;
using CourtBook.Domain.Enums;

namespace CourtBook.Application.Interfaces;

public interface ITermsService
{
    Task<TermsDocumentDto?> GetActiveTermsAsync(TermsType type);
}

using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Infrastructure.Services;

public class TermsService : ITermsService
{
    private readonly AppDbContext _db;

    public TermsService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<TermsDocumentDto?> GetActiveTermsAsync(TermsType type)
    {
        var doc = await _db.TermsDocuments
            .AsNoTracking()
            .Where(t => t.Type == type && t.IsActive)
            .OrderByDescending(t => t.PublishedAt)
            .FirstOrDefaultAsync();

        if (doc is null) return null;

        return new TermsDocumentDto
        {
            Id = doc.Id,
            Type = doc.Type,
            Version = doc.Version,
            Title = doc.Title,
            Content = doc.Content,
            PublishedAt = doc.PublishedAt
        };
    }
}

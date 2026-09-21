using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Infrastructure.Services;

public class AdminVenueService : IAdminVenueService
{
    private readonly AppDbContext _db;

    public AdminVenueService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<AdminVenueDto>> GetAllVenuesAsync(VenueApprovalStatus? status = null)
    {
        var query = _db.Venues
            .AsNoTracking()
            .Include(v => v.Owner)
            .Include(v => v.Courts)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(v => v.ApprovalStatus == status.Value);
        }

        var venues = await query
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync();

        return venues.Select(v => new AdminVenueDto
        {
            Id = v.Id,
            OwnerId = v.OwnerId,
            OwnerName = v.Owner.Name,
            OwnerEmail = v.Owner.Email,
            Name = v.Name,
            City = v.City,
            Area = v.Area,
            Address = v.Address,
            ApprovalStatus = v.ApprovalStatus,
            RejectionReason = v.RejectionReason,
            ApprovedAt = v.ApprovedAt,
            IsActive = v.IsActive,
            TotalCourts = v.Courts.Count,
            CreatedAt = v.CreatedAt
        }).ToList();
    }

    public async Task<AdminVenueDto?> ApproveVenueAsync(Guid adminId, Guid venueId)
    {
        var venue = await _db.Venues
            .Include(v => v.Owner)
            .Include(v => v.Courts)
            .FirstOrDefaultAsync(v => v.Id == venueId);

        if (venue is null) return null;

        venue.ApprovalStatus = VenueApprovalStatus.Approved;
        venue.ApprovedById = adminId;
        venue.ApprovedAt = DateTime.UtcNow;
        venue.RejectionReason = null;

        await _db.SaveChangesAsync();

        return new AdminVenueDto
        {
            Id = venue.Id,
            OwnerId = venue.OwnerId,
            OwnerName = venue.Owner.Name,
            OwnerEmail = venue.Owner.Email,
            Name = venue.Name,
            City = venue.City,
            Area = venue.Area,
            Address = venue.Address,
            ApprovalStatus = venue.ApprovalStatus,
            RejectionReason = venue.RejectionReason,
            ApprovedAt = venue.ApprovedAt,
            IsActive = venue.IsActive,
            TotalCourts = venue.Courts.Count,
            CreatedAt = venue.CreatedAt
        };
    }

    public async Task<AdminVenueDto?> RejectVenueAsync(Guid adminId, Guid venueId, RejectVenueRequest request)
    {
        var venue = await _db.Venues
            .Include(v => v.Owner)
            .Include(v => v.Courts)
            .FirstOrDefaultAsync(v => v.Id == venueId);

        if (venue is null) return null;

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("A rejection reason is required.");

        venue.ApprovalStatus = VenueApprovalStatus.Rejected;
        venue.RejectionReason = request.Reason.Trim();
        venue.ApprovedById = null;
        venue.ApprovedAt = null;

        await _db.SaveChangesAsync();

        return new AdminVenueDto
        {
            Id = venue.Id,
            OwnerId = venue.OwnerId,
            OwnerName = venue.Owner.Name,
            OwnerEmail = venue.Owner.Email,
            Name = venue.Name,
            City = venue.City,
            Area = venue.Area,
            Address = venue.Address,
            ApprovalStatus = venue.ApprovalStatus,
            RejectionReason = venue.RejectionReason,
            ApprovedAt = venue.ApprovedAt,
            IsActive = venue.IsActive,
            TotalCourts = venue.Courts.Count,
            CreatedAt = venue.CreatedAt
        };
    }
}

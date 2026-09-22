using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Infrastructure.Services;

public class AdminVenueService : IAdminVenueService
{
    private readonly AppDbContext _db;
    private readonly INotificationService? _notificationService;

    public AdminVenueService(AppDbContext db, INotificationService? notificationService = null)
    {
        _db = db;
        _notificationService = notificationService;
    }

    public async Task<AdminDashboardDto> GetDashboardAsync()
    {
        var totalVenues = await _db.Venues.CountAsync();
        var pendingVenues = await _db.Venues.CountAsync(v => v.ApprovalStatus == VenueApprovalStatus.Pending);
        var approvedVenues = await _db.Venues.CountAsync(v => v.ApprovalStatus == VenueApprovalStatus.Approved);
        var rejectedVenues = await _db.Venues.CountAsync(v => v.ApprovalStatus == VenueApprovalStatus.Rejected);
        var totalCourts = await _db.Courts.CountAsync();
        var totalBookings = await _db.Bookings.CountAsync();
        var totalPlayers = await _db.Users.CountAsync(u => u.Role == Role.Client);
        var totalOwners = await _db.Users.CountAsync(u => u.Role == Role.Owner);

        var recentVenues = await _db.Venues
            .AsNoTracking()
            .Include(v => v.Owner)
            .Include(v => v.Courts)
            .OrderByDescending(v => v.CreatedAt)
            .Take(8)
            .ToListAsync();

        var recentSubmissions = recentVenues.Select(v => new AdminVenueDto
        {
            Id = v.Id,
            OwnerId = v.OwnerId,
            OwnerName = v.Owner.Name,
            OwnerEmail = v.Owner.Email,
            OwnerPhone = v.Owner.Phone,
            Name = v.Name,
            City = v.City,
            Area = v.Area,
            Address = v.Address,
            ApprovalStatus = v.ApprovalStatus,
            RejectionReason = v.RejectionReason,
            ApprovedAt = v.ApprovedAt,
            IsActive = v.IsActive,
            TotalCourts = v.Courts.Count,
            CreatedAt = v.CreatedAt,
            Sports = v.Courts.Select(c => c.SportType.ToString()).Distinct().ToList()
        }).ToList();

        var recentAuditLogs = await _db.AuditLogs
            .AsNoTracking()
            .Include(a => a.User)
            .OrderByDescending(a => a.Timestamp)
            .Take(10)
            .ToListAsync();

        var recentActivity = recentAuditLogs.Select(a => new AdminModerationActionDto
        {
            Id = a.Id,
            Action = a.Action,
            EntityName = a.EntityName,
            EntityId = a.EntityId,
            Details = a.Details,
            AdminName = a.User?.Name ?? "System Admin",
            Timestamp = a.Timestamp
        }).ToList();

        return new AdminDashboardDto
        {
            TotalVenues = totalVenues,
            PendingVenues = pendingVenues,
            ApprovedVenues = approvedVenues,
            RejectedVenues = rejectedVenues,
            TotalCourts = totalCourts,
            TotalBookings = totalBookings,
            TotalPlayers = totalPlayers,
            TotalOwners = totalOwners,
            RecentSubmissions = recentSubmissions,
            RecentActivity = recentActivity
        };
    }

    public async Task<List<AdminVenueDto>> GetAllVenuesAsync(VenueApprovalStatus? status = null, string? search = null)
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

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(v => v.Name.ToLower().Contains(term)
                                  || v.City.ToLower().Contains(term)
                                  || v.Area.ToLower().Contains(term)
                                  || v.Owner.Name.ToLower().Contains(term)
                                  || v.Owner.Email.ToLower().Contains(term));
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
            OwnerPhone = v.Owner.Phone,
            Name = v.Name,
            City = v.City,
            Area = v.Area,
            Address = v.Address,
            ApprovalStatus = v.ApprovalStatus,
            RejectionReason = v.RejectionReason,
            ApprovedAt = v.ApprovedAt,
            IsActive = v.IsActive,
            TotalCourts = v.Courts.Count,
            CreatedAt = v.CreatedAt,
            Sports = v.Courts.Select(c => c.SportType.ToString()).Distinct().ToList()
        }).ToList();
    }

    public async Task<AdminVenueDetailsDto?> GetVenueDetailsAsync(Guid venueId)
    {
        var venue = await _db.Venues
            .AsNoTracking()
            .Include(v => v.Owner)
            .Include(v => v.ApprovedBy)
            .Include(v => v.Courts)
            .Include(v => v.Amenities)
                .ThenInclude(va => va.Amenity)
            .Include(v => v.OperatingHours)
            .Include(v => v.CancellationPolicy)
            .FirstOrDefaultAsync(v => v.Id == venueId);

        if (venue is null) return null;

        return new AdminVenueDetailsDto
        {
            Id = venue.Id,
            OwnerId = venue.OwnerId,
            OwnerName = venue.Owner.Name,
            OwnerEmail = venue.Owner.Email,
            OwnerPhone = venue.Owner.Phone,
            Name = venue.Name,
            Description = venue.Description,
            City = venue.City,
            Area = venue.Area,
            Address = venue.Address,
            Country = venue.Country,
            Phone = venue.Phone,
            Email = venue.Email,
            ApprovalStatus = venue.ApprovalStatus,
            RejectionReason = venue.RejectionReason,
            ApprovedAt = venue.ApprovedAt,
            ApprovedByName = venue.ApprovedBy?.Name,
            IsActive = venue.IsActive,
            IsVerified = venue.IsVerified,
            CreatedAt = venue.CreatedAt,
            Courts = venue.Courts.Select(c => new AdminCourtDto
            {
                Id = c.Id,
                Name = c.Name,
                SportType = c.SportType,
                PricePerHour = c.PricePerHour,
                SurfaceType = c.SurfaceType,
                IsIndoor = c.IsIndoor,
                Capacity = c.Capacity,
                IsActive = c.IsActive
            }).ToList(),
            Amenities = venue.Amenities.Select(a => a.Amenity.Name).ToList(),
            OperatingHours = venue.OperatingHours.Select(oh => new AdminOperatingHourDto
            {
                DayOfWeek = oh.DayOfWeek,
                OpenTime = oh.OpenTime,
                CloseTime = oh.CloseTime,
                IsClosed = oh.IsClosed
            }).OrderBy(oh => oh.DayOfWeek).ToList(),
            CancellationPolicyDescription = venue.CancellationPolicy?.PolicyDescription,
            FreeCancellationHours = venue.CancellationPolicy?.FreeCancellationHours ?? 24,
            LateCancellationFeePercent = venue.CancellationPolicy?.LateCancellationFeePercent ?? 50m
        };
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

        // Record Audit Log
        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = adminId,
            Action = "ApproveVenue",
            EntityName = "Venue",
            EntityId = venue.Id.ToString(),
            Details = $"Approved facility '{venue.Name}' (Owner: {venue.Owner.Name})",
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        // Dispatch notification to Owner
        if (_notificationService is not null)
        {
            try
            {
                await _notificationService.SendNotificationAsync(
                    venue.OwnerId,
                    "Facility Approved! 🏟️",
                    $"Great news! Your facility '{venue.Name}' has been approved and is now active on PlaySpot.",
                    NotificationType.VenueApproved,
                    $"/owner/venues");
            }
            catch
            {
                // Log/swallow so transaction isn't broken if notification fails
            }
        }

        return new AdminVenueDto
        {
            Id = venue.Id,
            OwnerId = venue.OwnerId,
            OwnerName = venue.Owner.Name,
            OwnerEmail = venue.Owner.Email,
            OwnerPhone = venue.Owner.Phone,
            Name = venue.Name,
            City = venue.City,
            Area = venue.Area,
            Address = venue.Address,
            ApprovalStatus = venue.ApprovalStatus,
            RejectionReason = venue.RejectionReason,
            ApprovedAt = venue.ApprovedAt,
            IsActive = venue.IsActive,
            TotalCourts = venue.Courts.Count,
            CreatedAt = venue.CreatedAt,
            Sports = venue.Courts.Select(c => c.SportType.ToString()).Distinct().ToList()
        };
    }

    public async Task<AdminVenueDto?> RejectVenueAsync(Guid adminId, Guid venueId, RejectVenueRequest request)
    {
        var venue = await _db.Venues
            .Include(v => v.Owner)
            .Include(v => v.Courts)
            .FirstOrDefaultAsync(v => v.Id == venueId);

        if (venue is null) return null;

        if (string.IsNullOrWhiteSpace(request?.Reason))
            throw new ArgumentException("A rejection reason is required.");

        var trimmedReason = request.Reason.Trim();
        venue.ApprovalStatus = VenueApprovalStatus.Rejected;
        venue.RejectionReason = trimmedReason;
        venue.ApprovedById = null;
        venue.ApprovedAt = null;

        // Record Audit Log
        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = adminId,
            Action = "RejectVenue",
            EntityName = "Venue",
            EntityId = venue.Id.ToString(),
            Details = $"Rejected facility '{venue.Name}'. Reason: {trimmedReason}",
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        // Dispatch notification to Owner
        if (_notificationService is not null)
        {
            try
            {
                await _notificationService.SendNotificationAsync(
                    venue.OwnerId,
                    "Facility Review Update ⚠️",
                    $"Your facility '{venue.Name}' requires changes before approval: {trimmedReason}",
                    NotificationType.VenueRejected,
                    $"/owner/venues");
            }
            catch
            {
                // Log/swallow
            }
        }

        return new AdminVenueDto
        {
            Id = venue.Id,
            OwnerId = venue.OwnerId,
            OwnerName = venue.Owner.Name,
            OwnerEmail = venue.Owner.Email,
            OwnerPhone = venue.Owner.Phone,
            Name = venue.Name,
            City = venue.City,
            Area = venue.Area,
            Address = venue.Address,
            ApprovalStatus = venue.ApprovalStatus,
            RejectionReason = venue.RejectionReason,
            ApprovedAt = venue.ApprovedAt,
            IsActive = venue.IsActive,
            TotalCourts = venue.Courts.Count,
            CreatedAt = venue.CreatedAt,
            Sports = venue.Courts.Select(c => c.SportType.ToString()).Distinct().ToList()
        };
    }
}

using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Infrastructure.Services;

public class OwnerService : IOwnerService
{
    private readonly AppDbContext _db;

    public OwnerService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<OwnerDashboardSummaryDto> GetDashboardSummaryAsync(Guid ownerId)
    {
        var now = DateTime.UtcNow;
        var todayStart = DateTime.SpecifyKind(now.Date, DateTimeKind.Utc);
        var todayEnd = todayStart.AddDays(1);
        var weekStart = todayStart.AddDays(-(int)todayStart.DayOfWeek);
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // 1. Venues & Courts Counts
        var totalVenues = await _db.Venues
            .AsNoTracking()
            .CountAsync(v => v.OwnerId == ownerId);

        var totalCourts = await _db.Courts
            .AsNoTracking()
            .CountAsync(c => c.Venue.OwnerId == ownerId);

        // 2. Bookings Counts
        var ownerBookingsQuery = _db.Bookings
            .AsNoTracking()
            .Where(b => b.Court.Venue.OwnerId == ownerId);

        var upcomingCount = await ownerBookingsQuery
            .CountAsync(b => b.Status == BookingStatus.Confirmed && b.StartTime > now);

        var completedCount = await ownerBookingsQuery
            .CountAsync(b => b.Status == BookingStatus.Completed || (b.Status == BookingStatus.Confirmed && b.EndTime <= now));

        var cancelledCount = await ownerBookingsQuery
            .CountAsync(b => b.Status == BookingStatus.Cancelled);

        var bookingsToday = await ownerBookingsQuery
            .CountAsync(b => b.StartTime >= todayStart && b.StartTime < todayEnd);

        var bookingsThisWeek = await ownerBookingsQuery
            .CountAsync(b => b.StartTime >= weekStart);

        // 3. Revenue Calculations via direct database aggregation (realized/collected revenue)
        var totalRevenue = await CalculateRealizedRevenueAsync(ownerBookingsQuery);

        var revenueThisMonth = await CalculateRealizedRevenueAsync(
            ownerBookingsQuery.Where(b => b.StartTime >= monthStart));

        var revenueThisWeek = await CalculateRealizedRevenueAsync(
            ownerBookingsQuery.Where(b => b.StartTime >= weekStart));

        var revenueToday = await CalculateRealizedRevenueAsync(
            ownerBookingsQuery.Where(b => b.StartTime >= todayStart && b.StartTime < todayEnd));

        // 4. Most Booked Court
        var topCourt = await _db.Bookings
            .AsNoTracking()
            .Where(b => b.Court.Venue.OwnerId == ownerId && b.Status != BookingStatus.Cancelled)
            .GroupBy(b => new { b.CourtId, b.Court.Name })
            .Select(g => new { CourtName = g.Key.Name, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefaultAsync();

        // 5. Top Sport
        var topSportGroup = await _db.Bookings
            .AsNoTracking()
            .Where(b => b.Court.Venue.OwnerId == ownerId && b.Status != BookingStatus.Cancelled)
            .GroupBy(b => b.Court.SportType)
            .Select(g => new { Sport = g.Key.ToString(), Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefaultAsync();

        // 6. Recent Bookings (top 5)
        var recentBookingsEntities = await _db.Bookings
            .AsNoTracking()
            .Include(b => b.Court)
                .ThenInclude(c => c.Venue)
            .Include(b => b.User)
            .Where(b => b.Court.Venue.OwnerId == ownerId)
            .OrderByDescending(b => b.StartTime)
            .Take(5)
            .ToListAsync();

        var recentBookings = recentBookingsEntities.Select(MapToOwnerBookingDto).ToList();

        // 7. Owner Venues (top venues)
        var ownerVenues = await GetOwnerVenuesAsync(ownerId);

        var analytics = new OwnerAnalyticsDto
        {
            BookingsToday = bookingsToday,
            BookingsThisWeek = bookingsThisWeek,
            CompletedBookings = completedCount,
            CancelledBookings = cancelledCount,
            RevenueToday = revenueToday,
            RevenueThisWeek = revenueThisWeek,
            RevenueThisMonth = revenueThisMonth,
            TotalRevenue = totalRevenue,
            MostBookedCourtName = topCourt?.CourtName,
            MostBookedCourtCount = topCourt?.Count ?? 0,
            TopSport = topSportGroup?.Sport
        };

        return new OwnerDashboardSummaryDto
        {
            TotalVenues = totalVenues,
            TotalCourts = totalCourts,
            UpcomingBookingsCount = upcomingCount,
            CompletedBookingsCount = completedCount,
            CancelledBookingsCount = cancelledCount,
            TotalRevenue = totalRevenue,
            RevenueThisMonth = revenueThisMonth,
            RevenueThisWeek = revenueThisWeek,
            BookingsToday = bookingsToday,
            BookingsThisWeek = bookingsThisWeek,
            MostBookedCourtName = topCourt?.CourtName,
            MostBookedCourtCount = topCourt?.Count ?? 0,
            Venues = ownerVenues,
            RecentBookings = recentBookings,
            Analytics = analytics
        };
    }

    public async Task<List<OwnerVenueDto>> GetOwnerVenuesAsync(Guid ownerId)
    {
        var now = DateTime.UtcNow;

        var venues = await _db.Venues
            .AsNoTracking()
            .Include(v => v.Courts)
            .Include(v => v.Images)
            .Where(v => v.OwnerId == ownerId)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync();

        var venueIds = venues.Select(v => v.Id).ToList();

        // Upcoming bookings count per venue
        var upcomingCounts = await _db.Bookings
            .AsNoTracking()
            .Where(b => venueIds.Contains(b.Court.VenueId) && b.Status == BookingStatus.Confirmed && b.StartTime > now)
            .GroupBy(b => b.Court.VenueId)
            .Select(g => new { VenueId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.VenueId, x => x.Count);

        return venues.Select(v => new OwnerVenueDto
        {
            Id = v.Id,
            OwnerId = v.OwnerId,
            Name = v.Name,
            City = v.City,
            Area = v.Area,
            Address = v.Address,
            Country = v.Country,
            Sports = v.Courts.Select(c => c.SportType.ToString()).Distinct().ToList(),
            TotalCourts = v.Courts.Count,
            AverageRating = v.AverageRating,
            TotalReviews = v.TotalReviews,
            IsActive = v.IsActive,
            IsVerified = v.IsVerified,
            ApprovalStatus = v.ApprovalStatus,
            RejectionReason = v.RejectionReason,
            ApprovedAt = v.ApprovedAt,
            UpcomingBookingsCount = upcomingCounts.TryGetValue(v.Id, out var count) ? count : 0,
            PrimaryImageUrl = v.Images.FirstOrDefault(i => i.IsPrimary)?.ImageUrl ?? v.Images.FirstOrDefault()?.ImageUrl
        }).ToList();
    }

    public async Task<OwnerVenueDto?> GetOwnerVenueByIdAsync(Guid ownerId, Guid venueId)
    {
        var venue = await _db.Venues
            .AsNoTracking()
            .Include(v => v.Courts)
            .Include(v => v.Images)
            .FirstOrDefaultAsync(v => v.Id == venueId);

        if (venue is null) return null;

        if (venue.OwnerId != ownerId)
            throw new UnauthorizedAccessException("You do not have permission to view this facility.");

        var now = DateTime.UtcNow;
        var upcomingCount = await _db.Bookings
            .AsNoTracking()
            .CountAsync(b => b.Court.VenueId == venueId && b.Status == BookingStatus.Confirmed && b.StartTime > now);

        return new OwnerVenueDto
        {
            Id = venue.Id,
            OwnerId = venue.OwnerId,
            Name = venue.Name,
            City = venue.City,
            Area = venue.Area,
            Address = venue.Address,
            Country = venue.Country,
            Sports = venue.Courts.Select(c => c.SportType.ToString()).Distinct().ToList(),
            TotalCourts = venue.Courts.Count,
            AverageRating = venue.AverageRating,
            TotalReviews = venue.TotalReviews,
            IsActive = venue.IsActive,
            IsVerified = venue.IsVerified,
            ApprovalStatus = venue.ApprovalStatus,
            RejectionReason = venue.RejectionReason,
            ApprovedAt = venue.ApprovedAt,
            UpcomingBookingsCount = upcomingCount,
            PrimaryImageUrl = venue.Images.FirstOrDefault(i => i.IsPrimary)?.ImageUrl ?? venue.Images.FirstOrDefault()?.ImageUrl
        };
    }

    public async Task<PagedResult<OwnerBookingDto>> GetOwnerBookingsAsync(Guid ownerId, OwnerBookingQueryRequest request)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 10 : (request.PageSize > 50 ? 50 : request.PageSize);
        var now = DateTime.UtcNow;

        var query = _db.Bookings
            .AsNoTracking()
            .Include(b => b.Court)
                .ThenInclude(c => c.Venue)
            .Include(b => b.User)
            .Where(b => b.Court.Venue.OwnerId == ownerId);

        // Venue filter
        if (request.VenueId.HasValue && request.VenueId.Value != Guid.Empty)
        {
            query = query.Where(b => b.Court.VenueId == request.VenueId.Value);
        }

        // Status filter
        var status = (request.Status ?? "all").Trim().ToLowerInvariant();
        if (status == "upcoming")
        {
            query = query.Where(b => b.Status == BookingStatus.Confirmed && b.StartTime > now);
        }
        else if (status == "completed")
        {
            query = query.Where(b => b.Status == BookingStatus.Completed || (b.Status == BookingStatus.Confirmed && b.EndTime <= now));
        }
        else if (status == "cancelled")
        {
            query = query.Where(b => b.Status == BookingStatus.Cancelled);
        }

        // Sport filter
        if (!string.IsNullOrWhiteSpace(request.Sport) && Enum.TryParse<SportType>(request.Sport, true, out var sport))
        {
            query = query.Where(b => b.Court.SportType == sport);
        }

        // Date filter
        if (request.Date.HasValue)
        {
            var dateStart = DateTime.SpecifyKind(request.Date.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var dateEnd = dateStart.AddDays(1);
            query = query.Where(b => b.StartTime >= dateStart && b.StartTime < dateEnd);
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(b => b.StartTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = items.Select(MapToOwnerBookingDto).ToList();
        return PagedResult<OwnerBookingDto>.From(dtos, totalCount, page, pageSize);
    }

    public async Task<OwnerBookingDto?> GetOwnerBookingByIdAsync(Guid ownerId, Guid bookingId)
    {
        var booking = await _db.Bookings
            .AsNoTracking()
            .Include(b => b.Court)
                .ThenInclude(c => c.Venue)
            .Include(b => b.User)
            .FirstOrDefaultAsync(b => b.Id == bookingId);

        if (booking is null) return null;

        if (booking.Court?.Venue?.OwnerId != ownerId)
            throw new UnauthorizedAccessException("You do not have permission to view this reservation.");

        return MapToOwnerBookingDto(booking);
    }

    public async Task<OwnerAnalyticsDto> GetOwnerAnalyticsAsync(Guid ownerId)
    {
        var summary = await GetDashboardSummaryAsync(ownerId);
        return summary.Analytics;
    }

    private static OwnerBookingDto MapToOwnerBookingDto(Booking b)
    {
        var now = DateTime.UtcNow;
        var effectiveStatus = b.Status.ToString();
        if (b.Status == BookingStatus.Confirmed && b.EndTime <= now)
        {
            effectiveStatus = BookingStatus.Completed.ToString();
        }

        return new OwnerBookingDto
        {
            Id = b.Id,
            BookingReference = b.BookingReference,
            VenueId = b.Court?.VenueId ?? Guid.Empty,
            VenueName = b.Court?.Venue?.Name ?? string.Empty,
            CourtId = b.CourtId,
            CourtName = b.Court?.Name ?? string.Empty,
            SportType = b.Court?.SportType.ToString() ?? string.Empty,
            PlayerId = b.UserId,
            PlayerName = b.IsManualBooking ? (b.CustomerName ?? "Phone Customer") : (b.User?.Name ?? "Player"),
            PlayerEmail = b.IsManualBooking ? string.Empty : (b.User?.Email ?? string.Empty),
            PlayerPhone = b.IsManualBooking ? (b.CustomerPhone ?? string.Empty) : (b.User?.Phone ?? string.Empty),
            StartTime = b.StartTime,
            EndTime = b.EndTime,
            DurationMinutes = (int)(b.EndTime - b.StartTime).TotalMinutes,
            TotalPrice = b.TotalPrice,
            PaymentStatus = b.PaymentStatus.ToString(),
            BookingStatus = effectiveStatus,
            CancellationReason = b.CancellationReason,
            CancelledAt = b.CancelledAt,
            Notes = b.Notes,
            CreatedAt = b.CreatedAt,
            IsManualBooking = b.IsManualBooking,
            CustomerName = b.CustomerName,
            CustomerPhone = b.CustomerPhone,
            IsCheckedIn = b.IsCheckedIn,
            CheckedInAt = b.CheckedInAt,
            DiscountAmount = b.DiscountAmount
        };
    }

    // ── Phase 4B: Venue Management ──────────────────────────────────────────

    public async Task<OwnerVenueDetailsDto?> GetOwnerVenueDetailsAsync(Guid ownerId, Guid venueId)
    {
        var venue = await _db.Venues
            .AsNoTracking()
            .Include(v => v.Courts)
                .ThenInclude(c => c.Schedules)
            .Include(v => v.Amenities)
                .ThenInclude(va => va.Amenity)
            .Include(v => v.Images)
            .Include(v => v.OperatingHours)
            .Include(v => v.CancellationPolicy)
            .FirstOrDefaultAsync(v => v.Id == venueId);

        if (venue is null) return null;

        if (venue.OwnerId != ownerId)
            throw new UnauthorizedAccessException("You do not have permission to view this facility.");

        var now = DateTime.UtcNow;
        var upcomingCount = await _db.Bookings
            .AsNoTracking()
            .CountAsync(b => b.Court.VenueId == venueId && b.Status == BookingStatus.Confirmed && b.StartTime > now);

        return new OwnerVenueDetailsDto
        {
            Id = venue.Id,
            OwnerId = venue.OwnerId,
            Name = venue.Name,
            Description = venue.Description,
            City = venue.City,
            Area = venue.Area,
            Address = venue.Address,
            Country = venue.Country,
            Phone = venue.Phone,
            Email = venue.Email,
            Website = venue.Website,
            Latitude = venue.Latitude,
            Longitude = venue.Longitude,
            AverageRating = venue.AverageRating,
            TotalReviews = venue.TotalReviews,
            IsActive = venue.IsActive,
            IsVerified = venue.IsVerified,
            ApprovalStatus = venue.ApprovalStatus,
            RejectionReason = venue.RejectionReason,
            ApprovedAt = venue.ApprovedAt,
            CreatedAt = venue.CreatedAt,
            UpcomingBookingsCount = upcomingCount,
            PrimaryImageUrl = venue.Images.FirstOrDefault(i => i.IsPrimary)?.ImageUrl ?? venue.Images.FirstOrDefault()?.ImageUrl,
            Sports = venue.Courts.Where(c => c.IsActive).Select(c => c.SportType.ToString()).Distinct().ToList(),
            TotalCourts = venue.Courts.Count,
            Courts = venue.Courts.Select(MapToCourtResponse).ToList(),
            Amenities = venue.Amenities.Select(va => new VenueAmenityDto
            {
                Id = va.AmenityId,
                Name = va.Amenity?.Name ?? "",
                Icon = va.Amenity?.Icon ?? "bi-check-circle",
                Category = va.Amenity?.Category ?? "General"
            }).ToList(),
            Images = venue.Images.OrderBy(i => i.DisplayOrder).Select(i => new VenueImageDto
            {
                Id = i.Id,
                ImageUrl = i.ImageUrl,
                IsPrimary = i.IsPrimary,
                DisplayOrder = i.DisplayOrder,
                Caption = i.Caption
            }).ToList(),
            OperatingHours = venue.OperatingHours.OrderBy(o => o.DayOfWeek).Select(o => new OperatingHourDto
            {
                DayOfWeek = o.DayOfWeek,
                DayName = o.DayOfWeek.ToString(),
                OpenTime = o.OpenTime.ToString("HH:mm"),
                CloseTime = o.CloseTime.ToString("HH:mm"),
                IsClosed = o.IsClosed
            }).ToList(),
            CancellationPolicy = venue.CancellationPolicy != null ? new CancellationPolicyDto
            {
                FreeCancellationHours = venue.CancellationPolicy.FreeCancellationHours,
                LateCancellationFeePercent = venue.CancellationPolicy.LateCancellationFeePercent,
                PolicyDescription = venue.CancellationPolicy.PolicyDescription
            } : null
        };
    }

    public async Task<OwnerVenueDto> CreateVenueAsync(Guid ownerId, CreateVenueRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Facility name is required.");
        if (string.IsNullOrWhiteSpace(request.City))
            throw new ArgumentException("City is required.");
        if (string.IsNullOrWhiteSpace(request.Address))
            throw new ArgumentException("Address is required.");

        var trimmedName = request.Name.Trim();
        var duplicate = await _db.Venues
            .AnyAsync(v => v.OwnerId == ownerId && v.Name.ToLower() == trimmedName.ToLower());
        if (duplicate)
            throw new InvalidOperationException("You already manage a facility with this name.");

        var venueId = Guid.NewGuid();
        var venue = new Venue
        {
            Id = venueId,
            OwnerId = ownerId,
            Name = trimmedName,
            Description = request.Description?.Trim() ?? string.Empty,
            City = request.City.Trim(),
            Area = request.Area?.Trim() ?? string.Empty,
            Address = request.Address.Trim(),
            Country = "Egypt",
            Phone = request.Phone?.Trim() ?? string.Empty,
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Website = string.IsNullOrWhiteSpace(request.Website) ? null : request.Website.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            IsActive = true,
            IsVerified = false,
            ApprovalStatus = VenueApprovalStatus.Pending,
            AverageRating = 0.0,
            TotalReviews = 0,
            CreatedAt = DateTime.UtcNow
        };

        // Amenities
        if (request.AmenityIds != null && request.AmenityIds.Any())
        {
            var validAmenityIds = await _db.Amenities
                .Where(a => request.AmenityIds.Contains(a.Id))
                .Select(a => a.Id)
                .Distinct()
                .ToListAsync();

            foreach (var aid in validAmenityIds)
            {
                venue.Amenities.Add(new VenueAmenity { VenueId = venueId, AmenityId = aid });
            }
        }

        // Initialize standard 7-day Operating Hours
        for (int i = 0; i < 7; i++)
        {
            var day = (DayOfWeek)i;
            venue.OperatingHours.Add(new OperatingHour
            {
                Id = Guid.NewGuid(),
                VenueId = venueId,
                DayOfWeek = day,
                OpenTime = day == DayOfWeek.Friday ? new TimeOnly(14, 0) : new TimeOnly(8, 0),
                CloseTime = new TimeOnly(23, 0),
                IsClosed = false
            });
        }

        // Initialize standard Cancellation Policy
        venue.CancellationPolicy = new CancellationPolicy
        {
            Id = Guid.NewGuid(),
            VenueId = venueId,
            FreeCancellationHours = 24,
            LateCancellationFeePercent = 50.0m,
            PolicyDescription = "Free cancellation up to 24 hours before your booking."
        };

        _db.Venues.Add(venue);
        await _db.SaveChangesAsync();

        return new OwnerVenueDto
        {
            Id = venue.Id,
            OwnerId = venue.OwnerId,
            Name = venue.Name,
            City = venue.City,
            Area = venue.Area,
            Address = venue.Address,
            Country = venue.Country,
            Sports = [],
            TotalCourts = 0,
            AverageRating = 0.0,
            TotalReviews = 0,
            IsActive = true,
            IsVerified = false,
            ApprovalStatus = venue.ApprovalStatus,
            RejectionReason = venue.RejectionReason,
            ApprovedAt = venue.ApprovedAt,
            UpcomingBookingsCount = 0,
            PrimaryImageUrl = null
        };
    }

    public async Task<OwnerVenueDto> UpdateVenueAsync(Guid ownerId, Guid venueId, UpdateVenueRequest request)
    {
        var venue = await _db.Venues
            .Include(v => v.Amenities)
            .Include(v => v.Courts)
            .Include(v => v.Images)
            .FirstOrDefaultAsync(v => v.Id == venueId);

        if (venue is null)
            throw new KeyNotFoundException("Facility not found.");

        if (venue.OwnerId != ownerId)
            throw new UnauthorizedAccessException("You do not have permission to modify this facility.");

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Facility name is required.");
        if (string.IsNullOrWhiteSpace(request.City))
            throw new ArgumentException("City is required.");
        if (string.IsNullOrWhiteSpace(request.Address))
            throw new ArgumentException("Address is required.");

        var trimmedName = request.Name.Trim();
        var duplicate = await _db.Venues
            .AnyAsync(v => v.OwnerId == ownerId && v.Id != venueId && v.Name.ToLower() == trimmedName.ToLower());
        if (duplicate)
            throw new InvalidOperationException("Another facility already uses this name.");

        venue.Name = trimmedName;
        venue.Description = request.Description?.Trim() ?? string.Empty;
        venue.City = request.City.Trim();
        venue.Area = request.Area?.Trim() ?? string.Empty;
        venue.Address = request.Address.Trim();
        venue.Phone = request.Phone?.Trim() ?? string.Empty;
        venue.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        venue.Website = string.IsNullOrWhiteSpace(request.Website) ? null : request.Website.Trim();
        venue.Latitude = request.Latitude;
        venue.Longitude = request.Longitude;
        venue.IsActive = request.IsActive;

        // Amenities update
        if (request.AmenityIds != null)
        {
            _db.VenueAmenities.RemoveRange(venue.Amenities);
            var validAmenityIds = await _db.Amenities
                .Where(a => request.AmenityIds.Contains(a.Id))
                .Select(a => a.Id)
                .Distinct()
                .ToListAsync();

            foreach (var aid in validAmenityIds)
            {
                _db.VenueAmenities.Add(new VenueAmenity { VenueId = venueId, AmenityId = aid });
            }
        }

        await _db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var upcomingCount = await _db.Bookings
            .AsNoTracking()
            .CountAsync(b => b.Court.VenueId == venueId && b.Status == BookingStatus.Confirmed && b.StartTime > now);

        return new OwnerVenueDto
        {
            Id = venue.Id,
            OwnerId = venue.OwnerId,
            Name = venue.Name,
            City = venue.City,
            Area = venue.Area,
            Address = venue.Address,
            Country = venue.Country,
            Sports = venue.Courts.Where(c => c.IsActive).Select(c => c.SportType.ToString()).Distinct().ToList(),
            TotalCourts = venue.Courts.Count,
            AverageRating = venue.AverageRating,
            TotalReviews = venue.TotalReviews,
            IsActive = venue.IsActive,
            IsVerified = venue.IsVerified,
            ApprovalStatus = venue.ApprovalStatus,
            RejectionReason = venue.RejectionReason,
            ApprovedAt = venue.ApprovedAt,
            UpcomingBookingsCount = upcomingCount,
            PrimaryImageUrl = venue.Images.FirstOrDefault(i => i.IsPrimary)?.ImageUrl ?? venue.Images.FirstOrDefault()?.ImageUrl
        };
    }

    public async Task<DeactivateResultDto> DeactivateVenueAsync(Guid ownerId, Guid venueId)
    {
        var venue = await _db.Venues.FindAsync(venueId);
        if (venue is null)
            throw new KeyNotFoundException("Facility not found.");

        if (venue.OwnerId != ownerId)
            throw new UnauthorizedAccessException("You do not have permission to deactivate this facility.");

        var now = DateTime.UtcNow;
        var activeUpcomingBookings = await _db.Bookings
            .CountAsync(b => b.Court.VenueId == venueId && b.Status == BookingStatus.Confirmed && b.StartTime > now);

        if (activeUpcomingBookings > 0)
        {
            return new DeactivateResultDto
            {
                Success = false,
                EntityId = venueId,
                IsActive = venue.IsActive,
                ActiveUpcomingBookingsCount = activeUpcomingBookings,
                Message = $"Cannot deactivate facility: There are {activeUpcomingBookings} active upcoming reservation(s). Please resolve or cancel them first."
            };
        }

        venue.IsActive = false;
        await _db.SaveChangesAsync();

        return new DeactivateResultDto
        {
            Success = true,
            EntityId = venueId,
            IsActive = false,
            ActiveUpcomingBookingsCount = 0,
            Message = "Facility successfully deactivated."
        };
    }

    // ── Phase 4B: Court Management ──────────────────────────────────────────

    public async Task<List<CourtResponse>> GetOwnerVenueCourtsAsync(Guid ownerId, Guid venueId)
    {
        var venue = await _db.Venues.FindAsync(venueId);
        if (venue is null)
            throw new KeyNotFoundException("Facility not found.");

        if (venue.OwnerId != ownerId)
            throw new UnauthorizedAccessException("You do not have permission to view courts for this facility.");

        var courts = await _db.Courts
            .AsNoTracking()
            .Include(c => c.Schedules)
            .Where(c => c.VenueId == venueId)
            .OrderBy(c => c.Name)
            .ToListAsync();

        return courts.Select(MapToCourtResponse).ToList();
    }

    public async Task<CourtResponse?> GetOwnerCourtByIdAsync(Guid ownerId, Guid courtId)
    {
        var court = await _db.Courts
            .AsNoTracking()
            .Include(c => c.Venue)
            .Include(c => c.Schedules)
            .FirstOrDefaultAsync(c => c.Id == courtId);

        if (court is null) return null;

        if (court.Venue.OwnerId != ownerId)
            throw new UnauthorizedAccessException("You do not have permission to view this court.");

        return MapToCourtResponse(court);
    }

    public async Task<CourtResponse> CreateCourtAsync(Guid ownerId, Guid venueId, CreateCourtRequest request)
    {
        var venue = await _db.Venues.Include(v => v.OperatingHours).FirstOrDefaultAsync(v => v.Id == venueId);
        if (venue is null)
            throw new KeyNotFoundException("Facility not found.");

        if (venue.OwnerId != ownerId)
            throw new UnauthorizedAccessException("You do not have permission to add courts to this facility.");

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Court name is required.");

        if (!Enum.TryParse<SportType>(request.SportType, true, out var sportType))
            throw new ArgumentException($"Invalid sport type: '{request.SportType}'.");

        if (request.PricePerHour <= 0)
            throw new ArgumentException("Price per hour must be greater than zero.");

        var trimmedName = request.Name.Trim();
        var duplicate = await _db.Courts
            .AnyAsync(c => c.VenueId == venueId && c.Name.ToLower() == trimmedName.ToLower());
        if (duplicate)
            throw new InvalidOperationException("A court with this name already exists in this facility.");

        var courtId = Guid.NewGuid();
        var court = new Court
        {
            Id = courtId,
            VenueId = venueId,
            Name = trimmedName,
            Description = request.Description?.Trim() ?? string.Empty,
            SportType = sportType,
            PricePerHour = request.PricePerHour,
            SurfaceType = string.IsNullOrWhiteSpace(request.SurfaceType) ? "Artificial Grass" : request.SurfaceType.Trim(),
            IsIndoor = request.IsIndoor,
            Capacity = request.Capacity <= 0 ? 10 : request.Capacity,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        // Initialize 7-day court schedules based on venue operating hours
        for (int i = 0; i < 7; i++)
        {
            var day = (DayOfWeek)i;
            var opHour = venue.OperatingHours?.FirstOrDefault(o => o.DayOfWeek == day);
            var openTime = opHour != null ? opHour.OpenTime : (day == DayOfWeek.Friday ? new TimeOnly(14, 0) : new TimeOnly(8, 0));
            var closeTime = opHour != null ? opHour.CloseTime : new TimeOnly(23, 0);

            court.Schedules.Add(new CourtSchedule
            {
                Id = Guid.NewGuid(),
                CourtId = courtId,
                DayOfWeek = day,
                OpenTime = openTime,
                CloseTime = closeTime
            });
        }

        _db.Courts.Add(court);
        await _db.SaveChangesAsync();

        return MapToCourtResponse(court);
    }

    public async Task<CourtResponse> UpdateCourtAsync(Guid ownerId, Guid courtId, UpdateCourtRequest request)
    {
        var court = await _db.Courts
            .Include(c => c.Venue)
            .Include(c => c.Schedules)
            .FirstOrDefaultAsync(c => c.Id == courtId);

        if (court is null)
            throw new KeyNotFoundException("Court not found.");

        if (court.Venue.OwnerId != ownerId)
            throw new UnauthorizedAccessException("You do not have permission to modify this court.");

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Court name is required.");

        if (!Enum.TryParse<SportType>(request.SportType, true, out var sportType))
            throw new ArgumentException($"Invalid sport type: '{request.SportType}'.");

        if (request.PricePerHour <= 0)
            throw new ArgumentException("Price per hour must be greater than zero.");

        var trimmedName = request.Name.Trim();
        var duplicate = await _db.Courts
            .AnyAsync(c => c.VenueId == court.VenueId && c.Id != courtId && c.Name.ToLower() == trimmedName.ToLower());
        if (duplicate)
            throw new InvalidOperationException("A court with this name already exists in this facility.");

        court.Name = trimmedName;
        court.Description = request.Description?.Trim() ?? string.Empty;
        court.SportType = sportType;
        court.PricePerHour = request.PricePerHour; // Preserves historical bookings' TotalPrice!
        court.SurfaceType = string.IsNullOrWhiteSpace(request.SurfaceType) ? court.SurfaceType : request.SurfaceType.Trim();
        court.IsIndoor = request.IsIndoor;
        court.Capacity = request.Capacity <= 0 ? court.Capacity : request.Capacity;
        court.IsActive = request.IsActive;

        await _db.SaveChangesAsync();

        return MapToCourtResponse(court);
    }

    public async Task<DeactivateResultDto> DeactivateCourtAsync(Guid ownerId, Guid courtId)
    {
        var court = await _db.Courts.Include(c => c.Venue).FirstOrDefaultAsync(c => c.Id == courtId);
        if (court is null)
            throw new KeyNotFoundException("Court not found.");

        if (court.Venue.OwnerId != ownerId)
            throw new UnauthorizedAccessException("You do not have permission to deactivate this court.");

        var now = DateTime.UtcNow;
        var activeUpcomingBookings = await _db.Bookings
            .CountAsync(b => b.CourtId == courtId && b.Status == BookingStatus.Confirmed && b.StartTime > now);

        if (activeUpcomingBookings > 0)
        {
            return new DeactivateResultDto
            {
                Success = false,
                EntityId = courtId,
                IsActive = court.IsActive,
                ActiveUpcomingBookingsCount = activeUpcomingBookings,
                Message = $"Cannot deactivate court: There are {activeUpcomingBookings} active upcoming reservation(s). Please cancel or reassign them first."
            };
        }

        court.IsActive = false;
        await _db.SaveChangesAsync();

        return new DeactivateResultDto
        {
            Success = true,
            EntityId = courtId,
            IsActive = false,
            ActiveUpcomingBookingsCount = 0,
            Message = "Court successfully deactivated."
        };
    }

    // ── Phase 4B: Amenities Management ──────────────────────────────────────

    public async Task<List<AmenityDto>> GetAmenitiesCatalogAsync()
    {
        var amenities = await _db.Amenities
            .AsNoTracking()
            .OrderBy(a => a.Category)
            .ThenBy(a => a.Name)
            .ToListAsync();

        return amenities.Select(a => new AmenityDto
        {
            Id = a.Id,
            Name = a.Name,
            Icon = a.Icon,
            Category = a.Category,
            IsSelected = false
        }).ToList();
    }

    public async Task<List<VenueAmenityDto>> UpdateVenueAmenitiesAsync(Guid ownerId, Guid venueId, List<Guid> amenityIds)
    {
        var venue = await _db.Venues
            .Include(v => v.Amenities)
                .ThenInclude(va => va.Amenity)
            .FirstOrDefaultAsync(v => v.Id == venueId);

        if (venue is null)
            throw new KeyNotFoundException("Facility not found.");

        if (venue.OwnerId != ownerId)
            throw new UnauthorizedAccessException("You do not have permission to modify amenities for this facility.");

        _db.VenueAmenities.RemoveRange(venue.Amenities);

        var validAmenities = await _db.Amenities
            .Where(a => amenityIds.Contains(a.Id))
            .Distinct()
            .ToListAsync();

        foreach (var amenity in validAmenities)
        {
            _db.VenueAmenities.Add(new VenueAmenity
            {
                VenueId = venueId,
                AmenityId = amenity.Id
            });
        }

        await _db.SaveChangesAsync();

        return validAmenities.Select(a => new VenueAmenityDto
        {
            Id = a.Id,
            Name = a.Name,
            Icon = a.Icon,
            Category = a.Category
        }).ToList();
    }

    // ── Phase 4B: Image Management ──────────────────────────────────────────

    public async Task<VenueImageDto> AddVenueImageAsync(Guid ownerId, Guid venueId, AddVenueImageRequest request)
    {
        var venue = await _db.Venues
            .Include(v => v.Images)
            .FirstOrDefaultAsync(v => v.Id == venueId);

        if (venue is null)
            throw new KeyNotFoundException("Facility not found.");

        if (venue.OwnerId != ownerId)
            throw new UnauthorizedAccessException("You do not have permission to manage images for this facility.");

        if (string.IsNullOrWhiteSpace(request.ImageUrl))
            throw new ArgumentException("Image URL is required.");

        var imageUrl = request.ImageUrl.Trim();
        // Safe path validation: must start with /images/ or http:// or https://
        if (!imageUrl.StartsWith("/images/", StringComparison.OrdinalIgnoreCase) &&
            !imageUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !imageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Image URL must be a valid relative path (/images/...) or absolute HTTP/HTTPS URL.");
        }

        // Check duplicate image for this venue
        if (venue.Images.Any(i => i.ImageUrl.Equals(imageUrl, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("This image has already been added to the facility.");
        }

        var isPrimary = request.IsPrimary || !venue.Images.Any();
        if (isPrimary)
        {
            foreach (var img in venue.Images)
            {
                img.IsPrimary = false;
            }
        }

        var image = new VenueImage
        {
            Id = Guid.NewGuid(),
            VenueId = venueId,
            ImageUrl = imageUrl,
            Caption = request.Caption?.Trim(),
            IsPrimary = isPrimary,
            DisplayOrder = request.DisplayOrder == 0 ? venue.Images.Count + 1 : request.DisplayOrder,
            CreatedAt = DateTime.UtcNow
        };

        _db.VenueImages.Add(image);
        await _db.SaveChangesAsync();

        return new VenueImageDto
        {
            Id = image.Id,
            ImageUrl = image.ImageUrl,
            IsPrimary = image.IsPrimary,
            DisplayOrder = image.DisplayOrder,
            Caption = image.Caption
        };
    }

    public async Task<bool> DeleteVenueImageAsync(Guid ownerId, Guid venueId, Guid imageId)
    {
        var image = await _db.VenueImages
            .Include(i => i.Venue)
            .FirstOrDefaultAsync(i => i.Id == imageId);

        if (image is null) return false;

        if (image.VenueId != venueId || image.Venue.OwnerId != ownerId)
            throw new UnauthorizedAccessException("You do not have permission to delete this image.");

        _db.VenueImages.Remove(image);
        await _db.SaveChangesAsync();

        // If the deleted image was primary, make the first remaining image primary
        var remaining = await _db.VenueImages.Where(i => i.VenueId == venueId).OrderBy(i => i.DisplayOrder).FirstOrDefaultAsync();
        if (remaining != null && !await _db.VenueImages.AnyAsync(i => i.VenueId == venueId && i.IsPrimary))
        {
            remaining.IsPrimary = true;
            await _db.SaveChangesAsync();
        }

        return true;
    }

    public async Task<bool> SetPrimaryVenueImageAsync(Guid ownerId, Guid venueId, Guid imageId)
    {
        var venue = await _db.Venues
            .Include(v => v.Images)
            .FirstOrDefaultAsync(v => v.Id == venueId);

        if (venue is null) return false;

        if (venue.OwnerId != ownerId)
            throw new UnauthorizedAccessException("You do not have permission to manage images for this facility.");

        var target = venue.Images.FirstOrDefault(i => i.Id == imageId);
        if (target is null) return false;

        foreach (var img in venue.Images)
        {
            img.IsPrimary = (img.Id == imageId);
        }

        await _db.SaveChangesAsync();
        return true;
    }

    // ── Phase 4B: Operating Hours Management ────────────────────────────────

    public async Task<List<OperatingHourDto>> GetVenueOperatingHoursAsync(Guid ownerId, Guid venueId)
    {
        var venue = await _db.Venues
            .Include(v => v.OperatingHours)
            .FirstOrDefaultAsync(v => v.Id == venueId);

        if (venue is null)
            throw new KeyNotFoundException("Facility not found.");

        if (venue.OwnerId != ownerId)
            throw new UnauthorizedAccessException("You do not have permission to view operating hours for this facility.");

        return venue.OperatingHours.OrderBy(o => o.DayOfWeek).Select(o => new OperatingHourDto
        {
            DayOfWeek = o.DayOfWeek,
            DayName = o.DayOfWeek.ToString(),
            OpenTime = o.OpenTime.ToString("HH:mm"),
            CloseTime = o.CloseTime.ToString("HH:mm"),
            IsClosed = o.IsClosed
        }).ToList();
    }

    public async Task<List<OperatingHourDto>> UpdateVenueOperatingHoursAsync(Guid ownerId, Guid venueId, List<UpdateOperatingHourRequest> hours)
    {
        var venue = await _db.Venues
            .Include(v => v.OperatingHours)
            .FirstOrDefaultAsync(v => v.Id == venueId);

        if (venue is null)
            throw new KeyNotFoundException("Facility not found.");

        if (venue.OwnerId != ownerId)
            throw new UnauthorizedAccessException("You do not have permission to modify operating hours for this facility.");

        _db.OperatingHours.RemoveRange(venue.OperatingHours);

        foreach (var h in hours)
        {
            TimeOnly openTime = TimeOnly.TryParse(h.OpenTime, out var ot) ? ot : new TimeOnly(8, 0);
            TimeOnly closeTime = TimeOnly.TryParse(h.CloseTime, out var ct) ? ct : new TimeOnly(23, 0);

            _db.OperatingHours.Add(new OperatingHour
            {
                Id = Guid.NewGuid(),
                VenueId = venueId,
                DayOfWeek = h.DayOfWeek,
                OpenTime = openTime,
                CloseTime = closeTime,
                IsClosed = h.IsClosed
            });
        }

        await _db.SaveChangesAsync();

        return hours.Select(h => new OperatingHourDto
        {
            DayOfWeek = h.DayOfWeek,
            DayName = h.DayOfWeek.ToString(),
            OpenTime = h.OpenTime,
            CloseTime = h.CloseTime,
            IsClosed = h.IsClosed
        }).ToList();
    }

    private static CourtResponse MapToCourtResponse(Court c)
    {
        return new CourtResponse
        {
            Id = c.Id,
            VenueId = c.VenueId,
            Name = c.Name,
            Description = c.Description,
            SportType = c.SportType.ToString(),
            PricePerHour = c.PricePerHour,
            SurfaceType = c.SurfaceType,
            IsIndoor = c.IsIndoor,
            Capacity = c.Capacity,
            IsActive = c.IsActive,
            Schedules = c.Schedules?.Select(s => new ScheduleResponse
            {
                Id = s.Id,
                CourtId = s.CourtId,
                DayOfWeek = (int)s.DayOfWeek,
                OpenTime = s.OpenTime.ToString("HH:mm"),
                CloseTime = s.CloseTime.ToString("HH:mm")
            }).ToList() ?? []
        };
    }

    private static async Task<decimal> CalculateRealizedRevenueAsync(IQueryable<Booking> baseQuery)
    {
        // 1. Confirmed / Completed bookings where payment is realized
        var eligibleConfirmed = baseQuery
            .Where(b => (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Completed)
                     && b.PaymentStatus != PaymentStatus.Refunded
                     && b.PaymentStatus != PaymentStatus.Failed
                     && b.PaymentStatus != PaymentStatus.Cancelled
                     && b.PaymentStatus != PaymentStatus.Processing
                     && !(b.Payment != null && (b.Payment.Status == PaymentStatus.Processing || b.Payment.Status == PaymentStatus.Failed || b.Payment.Status == PaymentStatus.Cancelled))
                     && !(b.Payment != null && b.Payment.Method == PaymentMethod.PayAtFacility && b.PaymentStatus == PaymentStatus.Pending));

        // Full price for non-partially refunded
        var confirmedFull = await eligibleConfirmed
            .Where(b => b.PaymentStatus != PaymentStatus.PartiallyRefunded)
            .SumAsync(b => (decimal?)b.TotalPrice) ?? 0m;

        // Retained cancellation fee for partially refunded
        var confirmedPartial = await eligibleConfirmed
            .Where(b => b.PaymentStatus == PaymentStatus.PartiallyRefunded)
            .SumAsync(b => (decimal?)b.CancellationFee) ?? 0m;

        // 2. Cancelled bookings where cancellation fee was actually retained/collected
        var cancelledWithRetainedFee = await baseQuery
            .Where(b => b.Status == BookingStatus.Cancelled
                     && b.CancellationFee > 0m
                     && b.PaymentStatus != PaymentStatus.Refunded
                     && !(b.Payment != null && b.Payment.Method == PaymentMethod.PayAtFacility && b.PaymentStatus == PaymentStatus.Pending)
                     && !(b.Payment != null && (b.Payment.Status == PaymentStatus.Failed || b.Payment.Status == PaymentStatus.Processing || b.Payment.Status == PaymentStatus.Cancelled)))
            .SumAsync(b => (decimal?)b.CancellationFee) ?? 0m;

        return confirmedFull + confirmedPartial + cancelledWithRetainedFee;
    }

    // ── Phase 12: Manual / Phone Booking & Receptionist Quick Check-in ──────

    public async Task<BookingResponse> CreateManualBookingAsync(Guid ownerId, CreateManualBookingRequest request)
    {
        if (request.StartTime >= request.EndTime)
            throw new ArgumentException("StartTime must be before EndTime.");

        if (string.IsNullOrWhiteSpace(request.CustomerName))
            throw new ArgumentException("Customer name is required for manual bookings.");

        var court = await _db.Courts
            .Include(c => c.Venue)
            .Include(c => c.PriceRules)
            .FirstOrDefaultAsync(c => c.Id == request.CourtId && c.Venue.OwnerId == ownerId);

        if (court == null)
            throw new UnauthorizedAccessException("Court not found or does not belong to your facilities.");

        // Check for conflicts
        var requestDate = DateOnly.FromDateTime(request.StartTime);
        var requestStartTime = TimeOnly.FromDateTime(request.StartTime);
        var requestEndTime = TimeOnly.FromDateTime(request.EndTime);

        var hasOverlap = await _db.Bookings.AnyAsync(b =>
            b.CourtId == request.CourtId &&
            b.Status != BookingStatus.Cancelled &&
            b.StartTime < request.EndTime &&
            b.EndTime > request.StartTime);

        if (hasOverlap)
            throw new InvalidOperationException("Court is already booked for this time slot.");

        // Handle Addons
        decimal addonsTotal = 0;
        var bookingAddons = new List<BookingAddon>();
        if (request.Addons != null && request.Addons.Any())
        {
            var courtAddonIds = request.Addons.Select(a => a.CourtAddonId).ToList();
            var availableAddons = await _db.CourtAddons
                .Where(a => courtAddonIds.Contains(a.Id) && a.CourtId == court.Id && a.IsAvailable)
                .ToListAsync();

            foreach (var sel in request.Addons.Where(a => a.Quantity > 0))
            {
                var ca = availableAddons.FirstOrDefault(x => x.Id == sel.CourtAddonId);
                if (ca != null)
                {
                    var lineTotal = ca.Price * sel.Quantity;
                    addonsTotal += lineTotal;
                    bookingAddons.Add(new BookingAddon
                    {
                        Id = Guid.NewGuid(),
                        CourtAddonId = ca.Id,
                        Quantity = sel.Quantity,
                        UnitPrice = ca.Price,
                        TotalPrice = lineTotal,
                        CourtAddon = ca
                    });
                }
            }
        }

        // Calculate Price
        decimal totalPrice;
        if (request.CustomPrice.HasValue && request.CustomPrice.Value >= 0)
        {
            totalPrice = request.CustomPrice.Value + addonsTotal;
        }
        else
        {
            var basePrice = court.PricePerHour * (decimal)(request.EndTime - request.StartTime).TotalHours;
            var courtPrice = basePrice;
            var dayOfWeek = request.StartTime.DayOfWeek;
            var matchingRule = court.PriceRules.FirstOrDefault(pr =>
                (pr.DayOfWeek == null || pr.DayOfWeek == dayOfWeek) &&
                requestStartTime >= pr.StartTime && requestEndTime <= pr.EndTime);

            if (matchingRule != null)
                courtPrice = matchingRule.FixedPrice ?? basePrice * matchingRule.PriceMultiplier;

            totalPrice = courtPrice + addonsTotal;
        }

        var reference = $"PS-MAN-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}".Substring(0, 20).ToUpperInvariant();

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = reference,
            CourtId = request.CourtId,
            UserId = ownerId,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Status = BookingStatus.Confirmed,
            PaymentStatus = PaymentStatus.Completed,
            TotalPrice = Math.Round(totalPrice, 2),
            Notes = request.Notes,
            IsManualBooking = true,
            CustomerName = request.CustomerName.Trim(),
            CustomerPhone = request.CustomerPhone?.Trim(),
            IsCheckedIn = false,
            CheckedInAt = null,
            Court = court,
            CreatedAt = DateTime.UtcNow,
            BookingAddons = bookingAddons
        };

        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        return MapBookingToResponse(booking);
    }

    public async Task<QuickCheckInResult> QuickCheckInAsync(Guid ownerId, QuickCheckInRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BookingReference))
        {
            return new QuickCheckInResult
            {
                Success = false,
                Message = "Booking reference is required.",
                MessageAr = "يرجى إدخال كود الحجز."
            };
        }

        var cleanRef = request.BookingReference.Trim().ToUpperInvariant();
        var booking = await _db.Bookings
            .Include(b => b.Court).ThenInclude(c => c.Venue)
            .Include(b => b.User)
            .Include(b => b.BookingAddons).ThenInclude(ba => ba.CourtAddon)
            .Include(b => b.PromoCode)
            .FirstOrDefaultAsync(b => b.BookingReference.ToUpper() == cleanRef);

        if (booking == null)
        {
            return new QuickCheckInResult
            {
                Success = false,
                Message = $"No booking found with reference '{cleanRef}'.",
                MessageAr = $"لم يتم العثور على أي حجز بالكود '{cleanRef}'."
            };
        }

        if (booking.Court?.Venue?.OwnerId != ownerId)
        {
            return new QuickCheckInResult
            {
                Success = false,
                Message = "This booking belongs to another venue/owner.",
                MessageAr = "هذا الحجز لا يتبع أحد ملاعبك أو منشآتك."
            };
        }

        if (booking.Status == BookingStatus.Cancelled)
        {
            return new QuickCheckInResult
            {
                Success = false,
                Message = "This booking was cancelled.",
                MessageAr = "هذا الحجز ملغي ولا يمكن تسجيل الدخول به."
            };
        }

        if (booking.IsCheckedIn)
        {
            return new QuickCheckInResult
            {
                Success = true,
                Message = $"Already checked in at {booking.CheckedInAt:HH:mm} UTC.",
                MessageAr = $"تم تسجيل الحضور مسبقاً في تمام {booking.CheckedInAt:HH:mm} UTC.",
                Booking = MapBookingToResponse(booking)
            };
        }

        booking.IsCheckedIn = true;
        booking.CheckedInAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new QuickCheckInResult
        {
            Success = true,
            Message = "Check-in successful! Welcome player.",
            MessageAr = "تم تسجيل الحضور بنجاح! مرحباً باللاعب.",
            Booking = MapBookingToResponse(booking)
        };
    }

    private static BookingResponse MapBookingToResponse(Booking booking)
    {
        var court = booking.Court;
        var venue = court?.Venue;
        return new BookingResponse
        {
            Id = booking.Id,
            BookingReference = booking.BookingReference,
            CourtId = booking.CourtId,
            CourtName = court?.Name ?? string.Empty,
            SportType = court?.SportType.ToString() ?? string.Empty,
            VenueId = venue?.Id ?? Guid.Empty,
            VenueName = venue?.Name ?? string.Empty,
            VenueCity = venue?.City ?? string.Empty,
            VenueAddress = venue?.Address ?? string.Empty,
            VenuePhone = venue?.Phone ?? string.Empty,
            UserId = booking.UserId,
            UserName = booking.IsManualBooking ? (booking.CustomerName ?? "Phone Customer") : (booking.User?.Name ?? string.Empty),
            StartTime = booking.StartTime,
            EndTime = booking.EndTime,
            Status = booking.Status.ToString(),
            PaymentStatus = booking.PaymentStatus.ToString(),
            TotalPrice = booking.TotalPrice,
            Notes = booking.Notes,
            CreatedAt = booking.CreatedAt,
            IsManualBooking = booking.IsManualBooking,
            CustomerName = booking.CustomerName,
            CustomerPhone = booking.CustomerPhone,
            IsCheckedIn = booking.IsCheckedIn,
            CheckedInAt = booking.CheckedInAt,
            DiscountAmount = booking.DiscountAmount,
            PromoCode = booking.PromoCode?.Code,
            Addons = booking.BookingAddons?.Select(ba => new BookingAddonDto
            {
                Id = ba.Id,
                CourtAddonId = ba.CourtAddonId,
                AddonName = ba.CourtAddon?.Name ?? string.Empty,
                AddonNameAr = ba.CourtAddon?.NameAr,
                Quantity = ba.Quantity,
                UnitPrice = ba.UnitPrice,
                TotalPrice = ba.TotalPrice
            }).ToList() ?? [],
            GoogleMapsUrl = venue != null && venue.Latitude.HasValue && venue.Longitude.HasValue
                ? $"https://www.google.com/maps?q={venue.Latitude.Value},{venue.Longitude.Value}"
                : null
        };
    }
}

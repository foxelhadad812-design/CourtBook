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

        // 3. Revenue Calculations
        var allBookings = await ownerBookingsQuery
            .Include(b => b.Court)
                .ThenInclude(c => c.Venue)
                    .ThenInclude(v => v.CancellationPolicy)
            .Select(b => new
            {
                b.StartTime,
                b.EndTime,
                b.Status,
                b.TotalPrice,
                b.CancelledAt,
                FreeCancellationHours = b.Court.Venue.CancellationPolicy != null ? b.Court.Venue.CancellationPolicy.FreeCancellationHours : 24,
                LateFeePercent = b.Court.Venue.CancellationPolicy != null ? b.Court.Venue.CancellationPolicy.LateCancellationFeePercent : 50m
            })
            .ToListAsync();

        decimal totalRevenue = 0;
        decimal revenueThisMonth = 0;
        decimal revenueThisWeek = 0;
        decimal revenueToday = 0;

        foreach (var b in allBookings)
        {
            decimal effectiveRevenue = 0;

            if (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Completed)
            {
                effectiveRevenue = b.TotalPrice;
            }
            else if (b.Status == BookingStatus.Cancelled && b.CancelledAt.HasValue)
            {
                var hoursUntilStart = (b.StartTime - b.CancelledAt.Value).TotalHours;
                if (hoursUntilStart < b.FreeCancellationHours && hoursUntilStart > 0)
                {
                    // Late fee retained by venue
                    effectiveRevenue = Math.Round(b.TotalPrice * (b.LateFeePercent / 100m), 2);
                }
            }

            totalRevenue += effectiveRevenue;

            if (b.StartTime >= monthStart)
                revenueThisMonth += effectiveRevenue;

            if (b.StartTime >= weekStart)
                revenueThisWeek += effectiveRevenue;

            if (b.StartTime >= todayStart && b.StartTime < todayEnd)
                revenueToday += effectiveRevenue;
        }

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
            PlayerName = b.User?.Name ?? "Player",
            PlayerEmail = b.User?.Email ?? string.Empty,
            PlayerPhone = b.User?.Phone ?? string.Empty,
            StartTime = b.StartTime,
            EndTime = b.EndTime,
            DurationMinutes = (int)(b.EndTime - b.StartTime).TotalMinutes,
            TotalPrice = b.TotalPrice,
            PaymentStatus = b.PaymentStatus.ToString(),
            BookingStatus = effectiveStatus,
            CancellationReason = b.CancellationReason,
            CancelledAt = b.CancelledAt,
            Notes = b.Notes,
            CreatedAt = b.CreatedAt
        };
    }
}

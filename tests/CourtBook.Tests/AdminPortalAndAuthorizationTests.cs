using CourtBook.API.Controllers;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using CourtBook.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CourtBook.Tests;

public class AdminPortalAndAuthorizationTests
{
    private (AppDbContext db, IAdminVenueService adminService, IVenueService venueService, INotificationService notifService) CreateServices(string dbName)
    {
        var db = TestDbContextFactory.Create(dbName);
        var notifService = new NotificationService(db, NullLogger<NotificationService>.Instance);
        var adminService = new AdminVenueService(db, notifService);
        var venueService = new VenueService(db);
        return (db, adminService, venueService, notifService);
    }

    [Fact]
    public void AdminVenuesController_IsGuardedByAdminRole()
    {
        var authAttr = typeof(AdminVenuesController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .FirstOrDefault() as AuthorizeAttribute;

        Assert.NotNull(authAttr);
        Assert.Equal("Admin", authAttr.Roles);
    }

    [Fact]
    public async Task AdminDashboard_ReturnsAccurateMetricsAndRecentLogs()
    {
        var (db, adminService, _, _) = CreateServices(nameof(AdminDashboard_ReturnsAccurateMetricsAndRecentLogs));

        var admin = new User { Id = Guid.NewGuid(), Name = "Admin User", Email = "admin@test.com", PasswordHash = "h", Role = Role.Admin };
        var owner = new User { Id = Guid.NewGuid(), Name = "Owner User", Email = "owner@test.com", PasswordHash = "h", Role = Role.Owner };
        var player1 = new User { Id = Guid.NewGuid(), Name = "Player One", Email = "p1@test.com", PasswordHash = "h", Role = Role.Client };
        var player2 = new User { Id = Guid.NewGuid(), Name = "Player Two", Email = "p2@test.com", PasswordHash = "h", Role = Role.Client };
        db.Users.AddRange(admin, owner, player1, player2);

        var pendingVenue = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = owner.Id,
            Name = "Pending Arena",
            City = "Cairo",
            Area = "Nasr City",
            Address = "Road 1",
            ApprovalStatus = VenueApprovalStatus.Pending,
            IsActive = true
        };

        var approvedVenue = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = owner.Id,
            Name = "Approved Club",
            City = "Giza",
            Area = "Dokki",
            Address = "Road 2",
            ApprovalStatus = VenueApprovalStatus.Approved,
            IsActive = true
        };

        var court1 = new Court { Id = Guid.NewGuid(), VenueId = approvedVenue.Id, Name = "Court A", SportType = SportType.Football, PricePerHour = 150m, IsActive = true };
        var court2 = new Court { Id = Guid.NewGuid(), VenueId = approvedVenue.Id, Name = "Court B", SportType = SportType.Padel, PricePerHour = 300m, IsActive = true };

        db.Venues.AddRange(pendingVenue, approvedVenue);
        db.Courts.AddRange(court1, court2);
        await db.SaveChangesAsync();

        var dashboard = await adminService.GetDashboardAsync();

        Assert.Equal(2, dashboard.TotalVenues);
        Assert.Equal(1, dashboard.PendingVenues);
        Assert.Equal(1, dashboard.ApprovedVenues);
        Assert.Equal(0, dashboard.RejectedVenues);
        Assert.Equal(2, dashboard.TotalCourts);
        Assert.Equal(2, dashboard.TotalPlayers);
        Assert.Equal(1, dashboard.TotalOwners);
        Assert.NotEmpty(dashboard.RecentSubmissions);
    }

    [Fact]
    public async Task AdminApproveVenue_UpdatesStatus_RecordsAuditLog_NotifiesOwner_AndBecomesDiscoverable()
    {
        var (db, adminService, venueService, _) = CreateServices(nameof(AdminApproveVenue_UpdatesStatus_RecordsAuditLog_NotifiesOwner_AndBecomesDiscoverable));

        var adminId = Guid.NewGuid();
        var admin = new User { Id = adminId, Name = "System Admin", Email = "admin@courtbook.eg", PasswordHash = "h", Role = Role.Admin };
        var owner = new User { Id = Guid.NewGuid(), Name = "Facility Owner", Email = "owner@courtbook.eg", PasswordHash = "h", Role = Role.Owner };
        db.Users.AddRange(admin, owner);

        var venue = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = owner.Id,
            Name = "Olympic Cairo Complex",
            City = "Cairo",
            Area = "Maadi",
            Address = "Corniche El Nile",
            ApprovalStatus = VenueApprovalStatus.Pending,
            IsActive = true
        };

        var court = new Court { Id = Guid.NewGuid(), VenueId = venue.Id, Name = "Main Pitch", SportType = SportType.Football, PricePerHour = 250m, IsActive = true };
        db.Venues.Add(venue);
        db.Courts.Add(court);
        await db.SaveChangesAsync();

        // 1. Verify not yet discoverable before approval
        var searchBefore = await venueService.SearchAsync(new VenueSearchRequest { Search = "Olympic" });
        Assert.Empty(searchBefore.Items);

        // 2. Admin approves venue
        var approved = await adminService.ApproveVenueAsync(adminId, venue.Id);

        Assert.NotNull(approved);
        Assert.Equal(VenueApprovalStatus.Approved, approved.ApprovalStatus);
        Assert.NotNull(approved.ApprovedAt);

        // 3. Verify DB record updated
        var dbVenue = await db.Venues.FindAsync(venue.Id);
        Assert.NotNull(dbVenue);
        Assert.Equal(VenueApprovalStatus.Approved, dbVenue.ApprovalStatus);
        Assert.Equal(adminId, dbVenue.ApprovedById);

        // 4. Verify Audit Log recorded
        var audit = await db.AuditLogs.FirstOrDefaultAsync(a => a.EntityId == venue.Id.ToString());
        Assert.NotNull(audit);
        Assert.Equal("ApproveVenue", audit.Action);
        Assert.Equal(adminId, audit.UserId);

        // 5. Verify Owner Notification dispatched
        var notif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == owner.Id);
        Assert.NotNull(notif);
        Assert.Equal(NotificationType.VenueApproved, notif.Type);
        Assert.Contains("Olympic Cairo Complex", notif.Message);

        // 6. Verify venue is now publicly discoverable
        var searchAfter = await venueService.SearchAsync(new VenueSearchRequest { Search = "Olympic" });
        Assert.Single(searchAfter.Items);
        Assert.Equal("Olympic Cairo Complex", searchAfter.Items[0].Name);
    }

    [Fact]
    public async Task AdminRejectVenue_RequiresReason_UpdatesStatus_RecordsAuditLog_NotifiesOwner_AndRemainsHidden()
    {
        var (db, adminService, venueService, _) = CreateServices(nameof(AdminRejectVenue_RequiresReason_UpdatesStatus_RecordsAuditLog_NotifiesOwner_AndRemainsHidden));

        var adminId = Guid.NewGuid();
        var admin = new User { Id = adminId, Name = "System Admin", Email = "admin@courtbook.eg", PasswordHash = "h", Role = Role.Admin };
        var owner = new User { Id = Guid.NewGuid(), Name = "Facility Owner", Email = "owner@courtbook.eg", PasswordHash = "h", Role = Role.Owner };
        db.Users.AddRange(admin, owner);

        var venue = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = owner.Id,
            Name = "Rejected Arena",
            City = "Alexandria",
            Area = "Smouha",
            Address = "Victor Emmanuel",
            ApprovalStatus = VenueApprovalStatus.Pending,
            IsActive = true
        };

        var court = new Court { Id = Guid.NewGuid(), VenueId = venue.Id, Name = "Padel 1", SportType = SportType.Padel, PricePerHour = 200m, IsActive = true };
        db.Venues.Add(venue);
        db.Courts.Add(court);
        await db.SaveChangesAsync();

        // 1. Rejection without reason throws ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() => adminService.RejectVenueAsync(adminId, venue.Id, new RejectVenueRequest { Reason = "" }));
        await Assert.ThrowsAsync<ArgumentException>(() => adminService.RejectVenueAsync(adminId, venue.Id, new RejectVenueRequest { Reason = "   " }));

        // 2. Reject with valid reason
        var rejectionReason = "Court photos are blurry and operating schedule is incomplete.";
        var rejected = await adminService.RejectVenueAsync(adminId, venue.Id, new RejectVenueRequest { Reason = rejectionReason });

        Assert.NotNull(rejected);
        Assert.Equal(VenueApprovalStatus.Rejected, rejected.ApprovalStatus);
        Assert.Equal(rejectionReason, rejected.RejectionReason);

        // 3. Verify DB record
        var dbVenue = await db.Venues.FindAsync(venue.Id);
        Assert.NotNull(dbVenue);
        Assert.Equal(VenueApprovalStatus.Rejected, dbVenue.ApprovalStatus);
        Assert.Equal(rejectionReason, dbVenue.RejectionReason);

        // 4. Verify Audit Log recorded
        var audit = await db.AuditLogs.FirstOrDefaultAsync(a => a.EntityId == venue.Id.ToString() && a.Action == "RejectVenue");
        Assert.NotNull(audit);
        Assert.Equal(adminId, audit.UserId);
        Assert.Contains(rejectionReason, audit.Details);

        // 5. Verify Owner Notification dispatched with reason
        var notif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == owner.Id && n.Type == NotificationType.VenueRejected);
        Assert.NotNull(notif);
        Assert.Contains(rejectionReason, notif.Message);

        // 6. Verify rejected venue is NOT discoverable in marketplace search
        var search = await venueService.SearchAsync(new VenueSearchRequest { Search = "Rejected" });
        Assert.Empty(search.Items);
    }

    [Fact]
    public async Task AdminVenueDetails_ReturnsFullInspectionData()
    {
        var (db, adminService, _, _) = CreateServices(nameof(AdminVenueDetails_ReturnsFullInspectionData));

        var owner = new User { Id = Guid.NewGuid(), Name = "Hassan Owner", Email = "hassan@venue.eg", Phone = "01099998888", PasswordHash = "h", Role = Role.Owner };
        db.Users.Add(owner);

        var venue = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = owner.Id,
            Name = "Grand Sports Hub",
            Description = "Premium multi-sport facility in New Cairo",
            City = "Cairo",
            Area = "New Cairo",
            Address = "5th Settlement, North 90th St",
            Phone = "01234567890",
            ApprovalStatus = VenueApprovalStatus.Pending,
            IsActive = true
        };

        var court1 = new Court { Id = Guid.NewGuid(), VenueId = venue.Id, Name = "Indoor Padel 1", SportType = SportType.Padel, PricePerHour = 350m, SurfaceType = "Glass/Plexi", IsIndoor = true, Capacity = 4, IsActive = true };
        var court2 = new Court { Id = Guid.NewGuid(), VenueId = venue.Id, Name = "Turf Football 7v7", SportType = SportType.Football, PricePerHour = 500m, SurfaceType = "Artificial Turf", IsIndoor = false, Capacity = 14, IsActive = true };

        var amenity1 = new Amenity { Id = Guid.NewGuid(), Name = "Free Parking", Icon = "bi-p-square" };
        var amenity2 = new Amenity { Id = Guid.NewGuid(), Name = "Showers & Lockers", Icon = "bi-droplet" };

        var venueAmenity1 = new VenueAmenity { VenueId = venue.Id, AmenityId = amenity1.Id, Amenity = amenity1 };
        var venueAmenity2 = new VenueAmenity { VenueId = venue.Id, AmenityId = amenity2.Id, Amenity = amenity2 };

        var opHour = new OperatingHour { Id = Guid.NewGuid(), VenueId = venue.Id, DayOfWeek = DayOfWeek.Friday, OpenTime = new TimeOnly(14, 0), CloseTime = new TimeOnly(23, 0), IsClosed = false };
        var policy = new CancellationPolicy { Id = Guid.NewGuid(), VenueId = venue.Id, FreeCancellationHours = 12, LateCancellationFeePercent = 25m, PolicyDescription = "Flexible policy" };

        db.Amenities.AddRange(amenity1, amenity2);
        db.Venues.Add(venue);
        db.Courts.AddRange(court1, court2);
        db.VenueAmenities.AddRange(venueAmenity1, venueAmenity2);
        db.OperatingHours.Add(opHour);
        db.CancellationPolicies.Add(policy);
        await db.SaveChangesAsync();

        var details = await adminService.GetVenueDetailsAsync(venue.Id);

        Assert.NotNull(details);
        Assert.Equal("Grand Sports Hub", details.Name);
        Assert.Equal("Hassan Owner", details.OwnerName);
        Assert.Equal("01099998888", details.OwnerPhone);
        Assert.Equal(2, details.Courts.Count);
        Assert.Equal(2, details.Amenities.Count);
        Assert.Single(details.OperatingHours);
        Assert.Equal(12, details.FreeCancellationHours);
        Assert.Equal(25m, details.LateCancellationFeePercent);
    }
}

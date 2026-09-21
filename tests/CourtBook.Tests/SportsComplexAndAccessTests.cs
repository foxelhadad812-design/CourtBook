using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Services;
using Xunit;

namespace CourtBook.Tests;

public class SportsComplexAndAccessTests
{
    private class DummyTokenService : ITokenService
    {
        public string GenerateToken(User user) => "dummy-jwt-token";
    }

    [Fact]
    public async Task SportsComplex_SearchReturnsAccurateSportsSummaryBreakdown()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var owner = new User { Id = Guid.NewGuid(), Name = "Owner 1", Email = "owner1@test.com", Role = Role.Owner, PasswordHash = "h" };
        db.Users.Add(owner);

        var complex = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = owner.Id,
            Name = "Olympic Multi-Sport Hub",
            City = "Cairo",
            Area = "New Cairo",
            Address = "North 90 St",
            IsActive = true,
            ApprovalStatus = VenueApprovalStatus.Approved
        };

        // 3 Football, 2 Padel, 1 Tennis
        for (int i = 1; i <= 3; i++)
            complex.Courts.Add(new Court { Id = Guid.NewGuid(), VenueId = complex.Id, Name = $"Football {i}", SportType = SportType.Football, PricePerHour = 500, IsActive = true });

        for (int i = 1; i <= 2; i++)
            complex.Courts.Add(new Court { Id = Guid.NewGuid(), VenueId = complex.Id, Name = $"Padel {i}", SportType = SportType.Padel, PricePerHour = 400, IsActive = true });

        complex.Courts.Add(new Court { Id = Guid.NewGuid(), VenueId = complex.Id, Name = "Tennis 1", SportType = SportType.Tennis, PricePerHour = 350, IsActive = true });

        db.Venues.Add(complex);
        await db.SaveChangesAsync();

        var venueService = new VenueService(db);
        var result = await venueService.SearchAsync(new VenueSearchRequest());

        Assert.Single(result.Items);
        var venueCard = result.Items[0];
        Assert.Equal("Olympic Multi-Sport Hub", venueCard.Name);
        Assert.Equal(3, venueCard.SportsSummary.Count);

        var footballSummary = venueCard.SportsSummary.FirstOrDefault(s => s.Sport == "Football");
        Assert.NotNull(footballSummary);
        Assert.Equal(3, footballSummary.CourtCount);

        var padelSummary = venueCard.SportsSummary.FirstOrDefault(s => s.Sport == "Padel");
        Assert.NotNull(padelSummary);
        Assert.Equal(2, padelSummary.CourtCount);

        var tennisSummary = venueCard.SportsSummary.FirstOrDefault(s => s.Sport == "Tennis");
        Assert.NotNull(tennisSummary);
        Assert.Equal(1, tennisSummary.CourtCount);
    }

    [Fact]
    public async Task SportsComplex_SportFilterMatchesOnlyActiveCourtsOfThatSport()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var owner = new User { Id = Guid.NewGuid(), Name = "Owner", Email = "o@test.com", Role = Role.Owner, PasswordHash = "h" };
        db.Users.Add(owner);

        // Complex 1 has active Football, but inactive Padel
        var c1 = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = owner.Id,
            Name = "Complex 1 (Football Only Active)",
            City = "Cairo",
            Address = "Road 1",
            IsActive = true,
            ApprovalStatus = VenueApprovalStatus.Approved
        };
        c1.Courts.Add(new Court { Id = Guid.NewGuid(), VenueId = c1.Id, Name = "F1", SportType = SportType.Football, PricePerHour = 200, IsActive = true });
        c1.Courts.Add(new Court { Id = Guid.NewGuid(), VenueId = c1.Id, Name = "P1 Inactive", SportType = SportType.Padel, PricePerHour = 300, IsActive = false });

        // Complex 2 has active Padel
        var c2 = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = owner.Id,
            Name = "Complex 2 (Padel Active)",
            City = "Cairo",
            Address = "Road 2",
            IsActive = true,
            ApprovalStatus = VenueApprovalStatus.Approved
        };
        c2.Courts.Add(new Court { Id = Guid.NewGuid(), VenueId = c2.Id, Name = "P2", SportType = SportType.Padel, PricePerHour = 300, IsActive = true });

        db.Venues.AddRange(c1, c2);
        await db.SaveChangesAsync();

        var service = new VenueService(db);

        // Search Football -> Only Complex 1
        var footballRes = await service.SearchAsync(new VenueSearchRequest { Sport = "Football" });
        Assert.Single(footballRes.Items);
        Assert.Equal(c1.Id, footballRes.Items[0].Id);

        // Search Padel -> Only Complex 2 (Complex 1's padel court is inactive)
        var padelRes = await service.SearchAsync(new VenueSearchRequest { Sport = "Padel" });
        Assert.Single(padelRes.Items);
        Assert.Equal(c2.Id, padelRes.Items[0].Id);
    }

    [Fact]
    public async Task SportsComplex_PublicSearchExcludesPendingAndRejectedVenues()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var owner = new User { Id = Guid.NewGuid(), Name = "Owner", Email = "o@test.com", Role = Role.Owner, PasswordHash = "h" };
        db.Users.Add(owner);

        var approvedVenue = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = owner.Id,
            Name = "Approved Complex",
            City = "Cairo",
            Address = "A1",
            IsActive = true,
            ApprovalStatus = VenueApprovalStatus.Approved
        };

        var pendingVenue = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = owner.Id,
            Name = "Pending Complex",
            City = "Cairo",
            Address = "P1",
            IsActive = true,
            ApprovalStatus = VenueApprovalStatus.Pending
        };

        var rejectedVenue = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = owner.Id,
            Name = "Rejected Complex",
            City = "Cairo",
            Address = "R1",
            IsActive = true,
            ApprovalStatus = VenueApprovalStatus.Rejected,
            RejectionReason = "Missing trade license"
        };

        var inactiveApprovedVenue = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = owner.Id,
            Name = "Inactive Complex",
            City = "Cairo",
            Address = "I1",
            IsActive = false,
            ApprovalStatus = VenueApprovalStatus.Approved
        };

        db.Venues.AddRange(approvedVenue, pendingVenue, rejectedVenue, inactiveApprovedVenue);
        await db.SaveChangesAsync();

        var service = new VenueService(db);
        var res = await service.SearchAsync(new VenueSearchRequest());

        Assert.Single(res.Items);
        Assert.Equal("Approved Complex", res.Items[0].Name);
    }

    [Fact]
    public async Task Registration_AssignsClientOrOwnerRole_RejectsAdmin()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var authService = new AuthService(db, new DummyTokenService());

        // Register as Client
        var clientRes = await authService.RegisterAsync(new RegisterRequest
        {
            Name = "Test Client",
            Email = "client@example.com",
            Password = "Password123!",
            Role = "Client",
            AcceptTerms = true
        });
        Assert.Equal("Client", clientRes.Role);

        // Register as Owner
        var ownerRes = await authService.RegisterAsync(new RegisterRequest
        {
            Name = "Test Owner",
            Email = "owner@example.com",
            Password = "Password123!",
            Role = "Owner",
            AcceptTerms = true
        });
        Assert.Equal("Owner", ownerRes.Role);

        // Register as Admin must throw InvalidOperationException
        var adminEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            authService.RegisterAsync(new RegisterRequest
            {
                Name = "Hacker",
                Email = "admin@example.com",
                Password = "Password123!",
                Role = "Admin",
                AcceptTerms = true
            }));
        Assert.Contains("Administrator is not allowed", adminEx.Message);
    }

    [Fact]
    public async Task Registration_RequiresTermsAcceptance_AuditsAcceptanceRecord()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var authService = new AuthService(db, new DummyTokenService());

        // Seed active terms documents
        var playerTerms = new TermsDocument
        {
            Id = Guid.NewGuid(),
            Type = TermsType.Player,
            Version = "v1.0",
            Title = "Player Terms",
            Content = "PlaySpot Player Terms",
            IsActive = true,
            PublishedAt = DateTime.UtcNow
        };
        db.TermsDocuments.Add(playerTerms);
        await db.SaveChangesAsync();

        // Register with AcceptTerms = false -> throws InvalidOperationException
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            authService.RegisterAsync(new RegisterRequest
            {
                Name = "No Terms User",
                Email = "noterms@example.com",
                Password = "Password123!",
                Role = "Client",
                AcceptTerms = false
            }));
        Assert.Contains("accept the terms", ex.Message);

        // Register with AcceptTerms = true -> succeeds and logs TermsAcceptance
        var res = await authService.RegisterAsync(new RegisterRequest
        {
            Name = "Accepted Terms User",
            Email = "accepted@example.com",
            Password = "Password123!",
            Role = "Client",
            AcceptTerms = true
        });
        Assert.NotNull(res.Token);

        var user = db.Users.FirstOrDefault(u => u.Email == "accepted@example.com");
        Assert.NotNull(user);

        var acceptance = db.TermsAcceptances.FirstOrDefault(t => t.UserId == user.Id);
        Assert.NotNull(acceptance);
        Assert.Equal(playerTerms.Id, acceptance.TermsDocumentId);
    }

    [Fact]
    public async Task AdminVenueService_ApproveAndReject_UpdatesStatusCorrectly()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var owner = new User { Id = Guid.NewGuid(), Name = "Owner", Email = "o@test.com", Role = Role.Owner, PasswordHash = "h" };
        var admin = new User { Id = Guid.NewGuid(), Name = "Admin", Email = "a@test.com", Role = Role.Admin, PasswordHash = "h" };
        db.Users.AddRange(owner, admin);

        var pendingVenue = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = owner.Id,
            Name = "Under Review Arena",
            City = "Giza",
            Address = "Giza Road",
            IsActive = true,
            ApprovalStatus = VenueApprovalStatus.Pending
        };
        db.Venues.Add(pendingVenue);
        await db.SaveChangesAsync();

        var adminService = new AdminVenueService(db);

        // 1. Get all venues filtered by Pending
        var pendingList = await adminService.GetAllVenuesAsync(VenueApprovalStatus.Pending);
        Assert.Single(pendingList);
        Assert.Equal(pendingVenue.Id, pendingList[0].Id);

        // 2. Approve venue
        var approved = await adminService.ApproveVenueAsync(admin.Id, pendingVenue.Id);
        Assert.NotNull(approved);
        Assert.Equal(VenueApprovalStatus.Approved, approved.ApprovalStatus);
        Assert.NotNull(approved.ApprovedAt);

        // 3. Reject venue requires reason
        await Assert.ThrowsAsync<ArgumentException>(() =>
            adminService.RejectVenueAsync(admin.Id, pendingVenue.Id, new RejectVenueRequest { Reason = "" }));

        // 4. Reject with valid reason
        var rejected = await adminService.RejectVenueAsync(admin.Id, pendingVenue.Id, new RejectVenueRequest
        {
            Reason = "Incomplete safety certifications provided."
        });
        Assert.NotNull(rejected);
        Assert.Equal(VenueApprovalStatus.Rejected, rejected.ApprovalStatus);
        Assert.Equal("Incomplete safety certifications provided.", rejected.RejectionReason);
        Assert.Null(rejected.ApprovedAt);
    }

    [Fact]
    public async Task TermsService_ReturnsActiveTermsDocument()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var playerDoc = new TermsDocument
        {
            Id = Guid.NewGuid(),
            Type = TermsType.Player,
            Version = "v1.0",
            Title = "Player Agreement",
            Content = "Terms content here...",
            IsActive = true,
            PublishedAt = DateTime.UtcNow
        };
        var ownerDoc = new TermsDocument
        {
            Id = Guid.NewGuid(),
            Type = TermsType.FacilityOwner,
            Version = "v1.0",
            Title = "Owner Agreement",
            Content = "Owner terms content here...",
            IsActive = true,
            PublishedAt = DateTime.UtcNow
        };
        db.TermsDocuments.AddRange(playerDoc, ownerDoc);
        await db.SaveChangesAsync();

        var termsService = new TermsService(db);

        var fetchedPlayer = await termsService.GetActiveTermsAsync(TermsType.Player);
        Assert.NotNull(fetchedPlayer);
        Assert.Equal("Player Agreement", fetchedPlayer.Title);

        var fetchedOwner = await termsService.GetActiveTermsAsync(TermsType.FacilityOwner);
        Assert.NotNull(fetchedOwner);
        Assert.Equal("Owner Agreement", fetchedOwner.Title);
    }

    [Fact]
    public async Task OwnerVenueCreation_DefaultsToPendingApproval()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var owner = new User { Id = Guid.NewGuid(), Name = "Owner", Email = "owner@test.com", Role = Role.Owner, PasswordHash = "h" };
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        var ownerService = new OwnerService(db);
        var created = await ownerService.CreateVenueAsync(owner.Id, new CreateVenueRequest
        {
            Name = "Brand New Facility",
            Description = "Brand new sports complex",
            City = "Alexandria",
            Area = "Smouha",
            Address = "Sporting St"
        });

        Assert.Equal(VenueApprovalStatus.Pending, created.ApprovalStatus);
        Assert.Null(created.ApprovedAt);
        Assert.Null(created.RejectionReason);

        // Verify in DB directly
        var dbVenue = await db.Venues.FindAsync(created.Id);
        Assert.NotNull(dbVenue);
        Assert.Equal(VenueApprovalStatus.Pending, dbVenue.ApprovalStatus);
    }
}

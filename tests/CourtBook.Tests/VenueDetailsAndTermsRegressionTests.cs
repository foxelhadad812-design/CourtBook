using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CourtBook.Tests;

public class VenueDetailsAndTermsRegressionTests
{
    private class FakeTokenService : ITokenService
    {
        public string GenerateToken(User user) => "fake-jwt-token";
    }

    [Fact]
    public async Task PublicHomeVenue_OpensCorrespondingDetails_WhenApprovedAndActive()
    {
        var db = TestDbContextFactory.Create(nameof(PublicHomeVenue_OpensCorrespondingDetails_WhenApprovedAndActive));
        var (venueId, courtId, ownerId, _) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var venueService = new VenueService(db);

        // 1. Home / Search query returns the venue
        var searchResult = await venueService.SearchAsync(new VenueSearchRequest { PageSize = 10 });
        Assert.NotNull(searchResult);
        var homeCard = searchResult.Items.FirstOrDefault(v => v.Id == venueId);
        Assert.NotNull(homeCard);
        Assert.Equal("Stars Arena", homeCard.Name);

        // 2. Clicking the card navigates with homeCard.Id to Details -> calls GetByIdAsync(homeCard.Id)
        var details = await venueService.GetByIdAsync(homeCard.Id);
        Assert.NotNull(details);
        Assert.Equal(homeCard.Id, details.Id);
        Assert.Equal("Stars Arena", details.Name);
        Assert.Equal("Cairo", details.City);
        Assert.Single(details.Courts);
        Assert.Equal(courtId, details.Courts[0].Id);
        Assert.Equal("Football Court 1", details.Courts[0].Name);
    }

    [Theory]
    [InlineData(true, VenueApprovalStatus.Pending)]
    [InlineData(true, VenueApprovalStatus.Rejected)]
    [InlineData(false, VenueApprovalStatus.Approved)]
    [InlineData(false, VenueApprovalStatus.Pending)]
    [InlineData(false, VenueApprovalStatus.Rejected)]
    public async Task InvalidOrUnapprovedVenue_RemainsUnavailable(bool isActive, VenueApprovalStatus status)
    {
        var db = TestDbContextFactory.Create($"VenueUnavailable_{isActive}_{status}");
        var owner = new User
        {
            Id = Guid.NewGuid(),
            Name = "Test Owner",
            Email = $"owner_{Guid.NewGuid():N}@test.com",
            PasswordHash = "hash",
            Role = Role.Owner
        };

        var venue = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = owner.Id,
            Name = "Restricted Complex",
            City = "Cairo",
            Address = "Giza",
            IsActive = isActive,
            ApprovalStatus = status
        };

        db.Users.Add(owner);
        db.Venues.Add(venue);
        await db.SaveChangesAsync();

        var venueService = new VenueService(db);

        // A non-approved or inactive venue must return null (404 Not Found)
        var result = await venueService.GetByIdAsync(venue.Id);
        Assert.Null(result);

        // Genuinely non-existent venue ID must also return null
        var nonExistentResult = await venueService.GetByIdAsync(Guid.NewGuid());
        Assert.Null(nonExistentResult);
    }

    [Fact]
    public async Task PlayerTerms_Loads_WithFullContentAndTranslations()
    {
        var db = TestDbContextFactory.Create(nameof(PlayerTerms_Loads_WithFullContentAndTranslations));
        var playerDoc = new TermsDocument
        {
            Id = Guid.NewGuid(),
            Type = TermsType.Player,
            Version = "1.0",
            Title = "PlaySpot Player Terms of Service & Code of Conduct",
            Content = "Full legal terms text for players...",
            IsActive = true,
            PublishedAt = DateTime.UtcNow
        };
        db.TermsDocuments.Add(playerDoc);
        await db.SaveChangesAsync();

        var termsService = new TermsService(db);
        var termsDto = await termsService.GetActiveTermsAsync(TermsType.Player);

        Assert.NotNull(termsDto);
        Assert.Equal(TermsType.Player, termsDto.Type);
        Assert.Equal("1.0", termsDto.Version);
        Assert.False(string.IsNullOrWhiteSpace(termsDto.Title));
        Assert.False(string.IsNullOrWhiteSpace(termsDto.Content));
        Assert.False(string.IsNullOrWhiteSpace(termsDto.TitleAr));
        Assert.False(string.IsNullOrWhiteSpace(termsDto.ContentAr));
        Assert.Contains("لاعب", termsDto.TitleAr);
    }

    [Fact]
    public async Task FacilityOwnerTerms_Loads_WithDistinctRequirements()
    {
        var db = TestDbContextFactory.Create(nameof(FacilityOwnerTerms_Loads_WithDistinctRequirements));
        var ownerDoc = new TermsDocument
        {
            Id = Guid.NewGuid(),
            Type = TermsType.FacilityOwner,
            Version = "1.0",
            Title = "PlaySpot Facility Owner Requirements & Partnership Agreement",
            Content = "Commercial partnership terms for sports complexes...",
            IsActive = true,
            PublishedAt = DateTime.UtcNow
        };
        db.TermsDocuments.Add(ownerDoc);
        await db.SaveChangesAsync();

        var termsService = new TermsService(db);
        var termsDto = await termsService.GetActiveTermsAsync(TermsType.FacilityOwner);

        Assert.NotNull(termsDto);
        Assert.Equal(TermsType.FacilityOwner, termsDto.Type);
        Assert.Equal("1.0", termsDto.Version);
        Assert.Contains("Owner", termsDto.Title);
        Assert.False(string.IsNullOrWhiteSpace(termsDto.TitleAr));
        Assert.False(string.IsNullOrWhiteSpace(termsDto.ContentAr));
        Assert.Contains("ملاك المنشآت", termsDto.TitleAr);
    }

    [Fact]
    public async Task Registration_WithoutTermsAcceptance_IsRejectedByServer()
    {
        var db = TestDbContextFactory.Create(nameof(Registration_WithoutTermsAcceptance_IsRejectedByServer));
        db.TermsDocuments.Add(new TermsDocument
        {
            Id = Guid.NewGuid(),
            Type = TermsType.Player,
            Version = "1.0",
            Title = "Player Terms",
            Content = "Terms content",
            IsActive = true,
            PublishedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var authService = new AuthService(db, new FakeTokenService());

        var validDob = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-22));

        // 1. Attempt to register without accepting terms
        var requestWithoutTerms = new RegisterRequest
        {
            Name = "Unconsenting Player",
            Email = "no_terms@test.com",
            Password = "Password123!",
            Role = "Client",
            DateOfBirth = validDob,
            AcceptTerms = false
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => authService.RegisterAsync(requestWithoutTerms));
        Assert.Contains("terms of service", ex.Message, StringComparison.OrdinalIgnoreCase);

        // 2. Register WITH terms acceptance succeeds
        var requestWithTerms = new RegisterRequest
        {
            Name = "Consenting Player",
            Email = "with_terms@test.com",
            Password = "Password123!",
            Role = "Client",
            DateOfBirth = validDob,
            AcceptTerms = true
        };

        var response = await authService.RegisterAsync(requestWithTerms);
        Assert.NotNull(response);
        Assert.Equal("Consenting Player", response.Name);

        var createdUser = await db.Users
            .Include(u => u.TermsAcceptances)
            .FirstOrDefaultAsync(u => u.Email == requestWithTerms.Email);

        Assert.NotNull(createdUser);
        Assert.Single(createdUser.TermsAcceptances);
        Assert.True(createdUser.TermsAcceptances.First().AcceptedAt <= DateTime.UtcNow);
    }
}

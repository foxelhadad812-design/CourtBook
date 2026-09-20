using CourtBook.Application.DTOs;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Services;
using Xunit;

namespace CourtBook.Tests;

public class VenueSearchAndFilterTests
{
    [Fact]
    public async Task SearchVenues_FiltersBySportAndCityProperly()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (venueId, _, ownerId, _) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        // Add a second venue in Alexandria with Padel court
        var alexVenue = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Name = "Alex Padel Club",
            City = "Alexandria",
            Area = "Smouha",
            Address = "Smouha Sporting St",
            IsActive = true,
            AverageRating = 4.9,
            TotalReviews = 15
        };
        alexVenue.Courts.Add(new Court
        {
            Id = Guid.NewGuid(),
            VenueId = alexVenue.Id,
            Name = "Padel 1",
            SportType = SportType.Padel,
            PricePerHour = 300m,
            IsActive = true
        });

        db.Venues.Add(alexVenue);
        await db.SaveChangesAsync();

        var venueService = new VenueService(db);

        // Search by Sport = "Padel"
        var padelResult = await venueService.SearchAsync(new VenueSearchRequest { Sport = "Padel" });
        Assert.Single(padelResult.Items);
        Assert.Equal("Alex Padel Club", padelResult.Items[0].Name);

        // Search by City = "Cairo"
        var cairoResult = await venueService.SearchAsync(new VenueSearchRequest { City = "Cairo" });
        Assert.Single(cairoResult.Items);
        Assert.Equal("Stars Arena", cairoResult.Items[0].Name);

        // Search with non-existent city
        var emptyResult = await venueService.SearchAsync(new VenueSearchRequest { City = "Aswan" });
        Assert.Empty(emptyResult.Items);
        Assert.Equal(0, emptyResult.TotalCount);
    }

    [Fact]
    public async Task SearchVenues_AppliesPaginationCorrectly()
    {
        var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var (_, _, ownerId, _) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        // Add 3 more venues
        for (int i = 1; i <= 3; i++)
        {
            db.Venues.Add(new Venue
            {
                Id = Guid.NewGuid(),
                OwnerId = ownerId,
                Name = $"Extra Venue {i}",
                City = "Giza",
                Address = $"Street {i}",
                IsActive = true
            });
        }
        await db.SaveChangesAsync();

        var venueService = new VenueService(db);

        // Total venues = 1 (from seed) + 3 = 4
        var page1 = await venueService.SearchAsync(new VenueSearchRequest { Page = 1, PageSize = 2 });
        Assert.Equal(4, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(2, page1.TotalPages);
        Assert.True(page1.HasNextPage);
        Assert.False(page1.HasPreviousPage);

        var page2 = await venueService.SearchAsync(new VenueSearchRequest { Page = 2, PageSize = 2 });
        Assert.Equal(2, page2.Items.Count);
        Assert.False(page2.HasNextPage);
        Assert.True(page2.HasPreviousPage);
    }
}

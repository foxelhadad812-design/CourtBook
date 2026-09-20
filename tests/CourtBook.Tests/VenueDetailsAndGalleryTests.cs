using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Services;
using Xunit;

namespace CourtBook.Tests;

public class VenueDetailsAndGalleryTests
{
    [Fact]
    public async Task GetByIdAsync_ReturnsEnrichedDetails_WhenVenueExists()
    {
        var db = TestDbContextFactory.Create(nameof(GetByIdAsync_ReturnsEnrichedDetails_WhenVenueExists));
        var (venueId, courtId, ownerId, _) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        // Add amenities, images, operating hours, and cancellation policy
        var amenity = new Amenity { Id = Guid.NewGuid(), Name = "Free WiFi", Icon = "bi-wifi", Category = "Comfort" };
        db.Amenities.Add(amenity);
        db.VenueAmenities.Add(new VenueAmenity { VenueId = venueId, AmenityId = amenity.Id });

        db.VenueImages.Add(new VenueImage
        {
            Id = Guid.NewGuid(),
            VenueId = venueId,
            ImageUrl = "/images/football.jpg",
            IsPrimary = true,
            DisplayOrder = 1,
            Caption = "Main Stadium Pitch"
        });

        db.CancellationPolicies.Add(new CancellationPolicy
        {
            Id = Guid.NewGuid(),
            VenueId = venueId,
            FreeCancellationHours = 12,
            LateCancellationFeePercent = 25.0m,
            PolicyDescription = "Flexible cancellation up to 12 hours before match."
        });

        await db.SaveChangesAsync();

        var service = new VenueService(db);
        var venue = await service.GetByIdAsync(venueId);

        Assert.NotNull(venue);
        Assert.Equal("Stars Arena", venue.Name);
        Assert.Equal("Cairo", venue.City);
        Assert.Single(venue.Courts);
        Assert.Single(venue.Amenities);
        Assert.Equal("Free WiFi", venue.Amenities[0].Name);
        Assert.Single(venue.Images);
        Assert.Equal("/images/football.jpg", venue.Images[0].ImageUrl);
        Assert.True(venue.Images[0].IsPrimary);
        Assert.NotNull(venue.CancellationPolicy);
        Assert.Equal(12, venue.CancellationPolicy.FreeCancellationHours);
        Assert.Equal(25.0m, venue.CancellationPolicy.LateCancellationFeePercent);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenVenueDoesNotExist()
    {
        var db = TestDbContextFactory.Create(nameof(GetByIdAsync_ReturnsNull_WhenVenueDoesNotExist));
        var service = new VenueService(db);

        var result = await service.GetByIdAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_ProvidesGracefulFallbacks_WhenImagesAndHoursNotExplicitlySeeded()
    {
        var db = TestDbContextFactory.Create(nameof(GetByIdAsync_ProvidesGracefulFallbacks_WhenImagesAndHoursNotExplicitlySeeded));
        var (venueId, _, _, _) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var service = new VenueService(db);
        var venue = await service.GetByIdAsync(venueId);

        Assert.NotNull(venue);
        // Fallback images generated for sports
        Assert.NotEmpty(venue.Images);
        Assert.Contains(venue.Images, i => i.ImageUrl.Contains("football"));

        // Fallback operating hours generated for 7 days
        Assert.NotEmpty(venue.OperatingHours);
        Assert.Equal(7, venue.OperatingHours.Count);
    }
}

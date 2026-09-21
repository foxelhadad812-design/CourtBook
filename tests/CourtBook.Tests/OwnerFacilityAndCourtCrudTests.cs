using CourtBook.Application.DTOs;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CourtBook.Tests;

public class OwnerFacilityAndCourtCrudTests
{
    private static (OwnerService service, Guid ownerAId, Guid ownerBId, Guid venueAId, Guid venueBId, Guid courtAId, Guid courtBId)
        SetupTestEnvironment(string dbName)
    {
        var db = TestDbContextFactory.Create(dbName);

        var ownerA = new User
        {
            Id = Guid.NewGuid(),
            Name = "Ahmed Mostafa (Owner A)",
            Email = $"ownerA_{Guid.NewGuid():N}@courtbook.eg",
            PasswordHash = "hash",
            Role = Role.Owner
        };

        var ownerB = new User
        {
            Id = Guid.NewGuid(),
            Name = "Sara Ibrahim (Owner B)",
            Email = $"ownerB_{Guid.NewGuid():N}@courtbook.eg",
            PasswordHash = "hash",
            Role = Role.Owner
        };

        var client = new User
        {
            Id = Guid.NewGuid(),
            Name = "Omar Client",
            Email = $"client_{Guid.NewGuid():N}@courtbook.eg",
            PasswordHash = "hash",
            Role = Role.Client
        };

        var venueA = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerA.Id,
            Name = "Venue Alpha",
            Description = "Alpha facility description",
            City = "Cairo",
            Area = "Maadi",
            Address = "10 Alpha St",
            Phone = "01000000001",
            IsActive = true
        };

        var venueB = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerB.Id,
            Name = "Venue Beta",
            Description = "Beta facility description",
            City = "Giza",
            Area = "Dokki",
            Address = "20 Beta St",
            Phone = "01000000002",
            IsActive = true
        };

        var courtA = new Court
        {
            Id = Guid.NewGuid(),
            VenueId = venueA.Id,
            Name = "Padel Court 1",
            Description = "Panoramic glass court",
            SportType = SportType.Padel,
            PricePerHour = 300m,
            SurfaceType = "Glass/Plexi",
            IsIndoor = false,
            Capacity = 4,
            IsActive = true
        };

        var courtB = new Court
        {
            Id = Guid.NewGuid(),
            VenueId = venueB.Id,
            Name = "Football Pitch 1",
            Description = "5-a-side turf pitch",
            SportType = SportType.Football,
            PricePerHour = 450m,
            SurfaceType = "Artificial Turf",
            IsIndoor = false,
            Capacity = 10,
            IsActive = true
        };

        var amenity1 = new Amenity { Id = Guid.NewGuid(), Name = "Free Parking", Icon = "bi-p-square", Category = "Comfort" };
        var amenity2 = new Amenity { Id = Guid.NewGuid(), Name = "Showers & Lockers", Icon = "bi-droplet", Category = "Comfort" };

        db.Users.AddRange(ownerA, ownerB, client);
        db.Venues.AddRange(venueA, venueB);
        db.Courts.AddRange(courtA, courtB);
        db.Amenities.AddRange(amenity1, amenity2);
        db.SaveChanges();

        var service = new OwnerService(db);
        return (service, ownerA.Id, ownerB.Id, venueA.Id, venueB.Id, courtA.Id, courtB.Id);
    }

    [Fact]
    public async Task Owner_CanCreateVenue_BelongsToAuthenticatedOwner()
    {
        var (service, ownerAId, _, _, _, _, _) = SetupTestEnvironment(nameof(Owner_CanCreateVenue_BelongsToAuthenticatedOwner));

        var request = new CreateVenueRequest
        {
            Name = "New Arena Olympic",
            Description = "State of the art sports complex",
            City = "Alexandria",
            Area = "Smouha",
            Address = "Smouha Sports St",
            Phone = "01099887766",
            Email = "arena@test.eg"
        };

        var created = await service.CreateVenueAsync(ownerAId, request);

        Assert.NotNull(created);
        Assert.Equal(ownerAId, created.OwnerId);
        Assert.Equal("New Arena Olympic", created.Name);
        Assert.Equal("Alexandria", created.City);
        Assert.True(created.IsActive);
    }

    [Fact]
    public async Task Owner_CanUpdateOwnVenue_Success()
    {
        var (service, ownerAId, _, venueAId, _, _, _) = SetupTestEnvironment(nameof(Owner_CanUpdateOwnVenue_Success));

        var updateReq = new UpdateVenueRequest
        {
            Name = "Venue Alpha Modernized",
            Description = "Updated description with new lighting",
            City = "Cairo",
            Area = "New Maadi",
            Address = "15 Updated St",
            Phone = "01011112222",
            IsActive = true
        };

        var updated = await service.UpdateVenueAsync(ownerAId, venueAId, updateReq);

        Assert.NotNull(updated);
        Assert.Equal("Venue Alpha Modernized", updated.Name);
        Assert.Equal("New Maadi", updated.Area);
    }

    [Fact]
    public async Task CrossOwner_OwnerACannotUpdate_OwnerB_Venue_ThrowsUnauthorizedAccessException()
    {
        var (service, ownerAId, _, _, venueBId, _, _) = SetupTestEnvironment(nameof(CrossOwner_OwnerACannotUpdate_OwnerB_Venue_ThrowsUnauthorizedAccessException));

        var hackRequest = new UpdateVenueRequest
        {
            Name = "Hacked Venue Beta",
            City = "Cairo",
            Address = "Attacker St"
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
        {
            await service.UpdateVenueAsync(ownerAId, venueBId, hackRequest);
        });
    }

    [Fact]
    public async Task CrossOwner_OwnerACannotDeactivate_OwnerB_Venue_ThrowsUnauthorizedAccessException()
    {
        var (service, ownerAId, _, _, venueBId, _, _) = SetupTestEnvironment(nameof(CrossOwner_OwnerACannotDeactivate_OwnerB_Venue_ThrowsUnauthorizedAccessException));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
        {
            await service.DeactivateVenueAsync(ownerAId, venueBId);
        });
    }

    [Fact]
    public async Task VenueDeactivation_Blocked_WhenActiveUpcomingBookingsExist()
    {
        var dbName = nameof(VenueDeactivation_Blocked_WhenActiveUpcomingBookingsExist);
        var db = TestDbContextFactory.Create(dbName);
        var (service, ownerAId, _, venueAId, _, courtAId, _) = SetupTestEnvironment(dbName);

        // Add an active upcoming booking to courtA in venueA
        var now = DateTime.UtcNow;
        var upcomingBooking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-UPCOMING-001",
            CourtId = courtAId,
            UserId = Guid.NewGuid(),
            StartTime = now.AddDays(2),
            EndTime = now.AddDays(2).AddHours(1),
            TotalPrice = 300m,
            Status = BookingStatus.Confirmed,
            PaymentStatus = PaymentStatus.Completed,
            CreatedAt = now
        };
        db.Bookings.Add(upcomingBooking);
        await db.SaveChangesAsync();

        var result = await service.DeactivateVenueAsync(ownerAId, venueAId);

        Assert.False(result.Success);
        Assert.True(result.IsActive);
        Assert.True(result.ActiveUpcomingBookingsCount > 0);
        Assert.Contains("active upcoming reservation", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Owner_CanCreateCourt_InOwnVenue_InitializesSchedules()
    {
        var (service, ownerAId, _, venueAId, _, _, _) = SetupTestEnvironment(nameof(Owner_CanCreateCourt_InOwnVenue_InitializesSchedules));

        var req = new CreateCourtRequest
        {
            Name = "Basketball Court 1",
            Description = "Indoor hardwood basketball court",
            SportType = "Basketball",
            PricePerHour = 350m,
            SurfaceType = "Wood",
            IsIndoor = true,
            Capacity = 10,
            IsActive = true
        };

        var created = await service.CreateCourtAsync(ownerAId, venueAId, req);

        Assert.NotNull(created);
        Assert.Equal(venueAId, created.VenueId);
        Assert.Equal("Basketball Court 1", created.Name);
        Assert.Equal("Basketball", created.SportType);
        Assert.Equal(350m, created.PricePerHour);
        Assert.True(created.IsIndoor);
        Assert.Equal(7, created.Schedules.Count); // 7 daily schedules initialized
    }

    [Fact]
    public async Task CrossOwner_OwnerACannotCreateCourt_InOwnerB_Venue_ThrowsUnauthorizedAccessException()
    {
        var (service, ownerAId, _, _, venueBId, _, _) = SetupTestEnvironment(nameof(CrossOwner_OwnerACannotCreateCourt_InOwnerB_Venue_ThrowsUnauthorizedAccessException));

        var req = new CreateCourtRequest
        {
            Name = "Sneaky Court",
            SportType = "Football",
            PricePerHour = 200m
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
        {
            await service.CreateCourtAsync(ownerAId, venueBId, req);
        });
    }

    [Fact]
    public async Task Owner_CanUpdateOwnCourt_Success()
    {
        var (service, ownerAId, _, _, _, courtAId, _) = SetupTestEnvironment(nameof(Owner_CanUpdateOwnCourt_Success));

        var updateReq = new UpdateCourtRequest
        {
            Name = "Padel Court 1 Super",
            Description = "Upgraded Mondo supercourt turf",
            SportType = "Padel",
            PricePerHour = 380m,
            SurfaceType = "Mondo Supercourt",
            IsIndoor = false,
            Capacity = 4,
            IsActive = true
        };

        var updated = await service.UpdateCourtAsync(ownerAId, courtAId, updateReq);

        Assert.NotNull(updated);
        Assert.Equal("Padel Court 1 Super", updated.Name);
        Assert.Equal(380m, updated.PricePerHour);
        Assert.Equal("Mondo Supercourt", updated.SurfaceType);
    }

    [Fact]
    public async Task CrossOwner_OwnerACannotUpdate_OwnerB_Court_ThrowsUnauthorizedAccessException()
    {
        var (service, ownerAId, _, _, _, _, courtBId) = SetupTestEnvironment(nameof(CrossOwner_OwnerACannotUpdate_OwnerB_Court_ThrowsUnauthorizedAccessException));

        var hackReq = new UpdateCourtRequest
        {
            Name = "Hijacked Court",
            SportType = "Football",
            PricePerHour = 50m
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
        {
            await service.UpdateCourtAsync(ownerAId, courtBId, hackReq);
        });
    }

    [Fact]
    public async Task CourtPriceUpdate_DoesNotAlter_HistoricalBookingsTotalPrice()
    {
        var dbName = nameof(CourtPriceUpdate_DoesNotAlter_HistoricalBookingsTotalPrice);
        var db = TestDbContextFactory.Create(dbName);
        var (service, ownerAId, _, _, _, courtAId, _) = SetupTestEnvironment(dbName);

        // Create a historical completed booking that cost 300 EGP
        var historicalBooking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-HISTORICAL-001",
            CourtId = courtAId,
            UserId = Guid.NewGuid(),
            StartTime = DateTime.UtcNow.AddDays(-10),
            EndTime = DateTime.UtcNow.AddDays(-10).AddHours(1),
            TotalPrice = 300.00m,
            Status = BookingStatus.Completed,
            PaymentStatus = PaymentStatus.Completed,
            CreatedAt = DateTime.UtcNow.AddDays(-11)
        };
        db.Bookings.Add(historicalBooking);
        await db.SaveChangesAsync();

        // Owner updates court price from 300 to 550 EGP
        var updateReq = new UpdateCourtRequest
        {
            Name = "Padel Court 1",
            SportType = "Padel",
            PricePerHour = 550.00m,
            SurfaceType = "Glass/Plexi",
            Capacity = 4,
            IsActive = true
        };
        await service.UpdateCourtAsync(ownerAId, courtAId, updateReq);

        // Verify historical booking price remains untouched at 300.00m!
        var reloadedBooking = await db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == historicalBooking.Id);
        Assert.NotNull(reloadedBooking);
        Assert.Equal(300.00m, reloadedBooking.TotalPrice); // Unchanged!
    }

    [Fact]
    public async Task CourtDeactivation_Blocked_WhenActiveUpcomingBookingsExist()
    {
        var dbName = nameof(CourtDeactivation_Blocked_WhenActiveUpcomingBookingsExist);
        var db = TestDbContextFactory.Create(dbName);
        var (service, ownerAId, _, _, _, courtAId, _) = SetupTestEnvironment(dbName);

        // Add upcoming booking to courtA
        var now = DateTime.UtcNow;
        var upcoming = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = "PS-UPCOMING-002",
            CourtId = courtAId,
            UserId = Guid.NewGuid(),
            StartTime = now.AddDays(3),
            EndTime = now.AddDays(3).AddHours(1),
            TotalPrice = 300m,
            Status = BookingStatus.Confirmed,
            PaymentStatus = PaymentStatus.Completed,
            CreatedAt = now
        };
        db.Bookings.Add(upcoming);
        await db.SaveChangesAsync();

        var result = await service.DeactivateCourtAsync(ownerAId, courtAId);

        Assert.False(result.Success);
        Assert.True(result.IsActive);
        Assert.True(result.ActiveUpcomingBookingsCount > 0);
        Assert.Contains("active upcoming reservation", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Owner_CanManageAmenitiesAndImages_ForOwnVenue()
    {
        var dbName = nameof(Owner_CanManageAmenitiesAndImages_ForOwnVenue);
        var db = TestDbContextFactory.Create(dbName);
        var (service, ownerAId, _, venueAId, _, _, _) = SetupTestEnvironment(dbName);

        // 1. Amenities Management
        var catalog = await service.GetAmenitiesCatalogAsync();
        Assert.True(catalog.Count >= 2);

        var selectedAmenityIds = catalog.Select(a => a.Id).ToList();
        var updatedAmenities = await service.UpdateVenueAmenitiesAsync(ownerAId, venueAId, selectedAmenityIds);
        Assert.Equal(selectedAmenityIds.Count, updatedAmenities.Count);

        // 2. Images Management
        var imgReq = new AddVenueImageRequest
        {
            ImageUrl = "/images/padel.jpg",
            Caption = "Pro Padel Court View",
            IsPrimary = true
        };
        var addedImg = await service.AddVenueImageAsync(ownerAId, venueAId, imgReq);
        Assert.NotNull(addedImg);
        Assert.True(addedImg.IsPrimary);
        Assert.Equal("/images/padel.jpg", addedImg.ImageUrl);

        // 3. Delete Image
        var deleted = await service.DeleteVenueImageAsync(ownerAId, venueAId, addedImg.Id);
        Assert.True(deleted);
    }

    [Fact]
    public async Task CrossOwner_OwnerACannotManageAmenitiesOrImages_ForOwnerB_Venue()
    {
        var (service, ownerAId, _, _, venueBId, _, _) = SetupTestEnvironment(nameof(CrossOwner_OwnerACannotManageAmenitiesOrImages_ForOwnerB_Venue));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
        {
            await service.UpdateVenueAmenitiesAsync(ownerAId, venueBId, [Guid.NewGuid()]);
        });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
        {
            await service.AddVenueImageAsync(ownerAId, venueBId, new AddVenueImageRequest { ImageUrl = "/images/football.jpg" });
        });
    }
}

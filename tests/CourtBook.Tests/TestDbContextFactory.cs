using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Tests;

public static class TestDbContextFactory
{
    public static AppDbContext Create(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public static async Task<(Guid venueId, Guid courtId, Guid ownerId, Guid clientId)> SeedBasicTestDataAsync(AppDbContext db)
    {
        var owner = new User
        {
            Id = Guid.NewGuid(),
            Name = "Ahmed Owner",
            Email = $"owner_{Guid.NewGuid():N}@test.com",
            PasswordHash = "hash",
            Role = Role.Owner
        };

        var client = new User
        {
            Id = Guid.NewGuid(),
            Name = "Omar Client",
            Email = $"client_{Guid.NewGuid():N}@test.com",
            PasswordHash = "hash",
            Role = Role.Client
        };

        var venue = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = owner.Id,
            Name = "Stars Arena",
            City = "Cairo",
            Area = "Maadi",
            Address = "10 Street",
            IsActive = true,
            ApprovalStatus = VenueApprovalStatus.Approved,
            AverageRating = 4.8,
            TotalReviews = 10
        };

        var court = new Court
        {
            Id = Guid.NewGuid(),
            VenueId = venue.Id,
            Name = "Football Court 1",
            SportType = SportType.Football,
            PricePerHour = 200m,
            IsActive = true
        };

        // Open 08:00 to 23:00 everyday
        for (int i = 0; i < 7; i++)
        {
            court.Schedules.Add(new CourtSchedule
            {
                Id = Guid.NewGuid(),
                CourtId = court.Id,
                DayOfWeek = (DayOfWeek)i,
                OpenTime = new TimeOnly(8, 0),
                CloseTime = new TimeOnly(23, 0)
            });
        }

        // Peak price rule: 18:00 - 22:00 -> 1.5x multiplier
        court.PriceRules.Add(new PriceRule
        {
            Id = Guid.NewGuid(),
            CourtId = court.Id,
            Name = "Peak Evening",
            StartTime = new TimeOnly(18, 0),
            EndTime = new TimeOnly(22, 0),
            PriceMultiplier = 1.5m,
            IsActive = true
        });

        db.Users.AddRange(owner, client);
        db.Venues.Add(venue);
        db.Courts.Add(court);
        await db.SaveChangesAsync();

        return (venue.Id, court.Id, owner.Id, client.Id);
    }
}

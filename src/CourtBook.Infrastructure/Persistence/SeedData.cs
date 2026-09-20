using BCrypt.Net;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CourtBook.Infrastructure.Persistence;

/// <summary>
/// Seeds the database with a minimal set of development data.
/// Only runs when no users exist, so it is safe to call on every startup.
/// </summary>
public static class SeedData
{
    /// <summary>
    /// Call this from Program.cs inside an IsDevelopment() guard to seed the DB.
    /// </summary>
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();

        // Apply any pending migrations automatically in development
        await db.Database.MigrateAsync();

        // Skip seeding if data already exists
        if (await db.Users.AnyAsync())
        {
            logger.LogInformation("Database already seeded — skipping.");
            return;
        }

        logger.LogInformation("Seeding development data...");

        // ── Users ─────────────────────────────────────────────────────────────

        var admin = new User
        {
            Id = Guid.NewGuid(),
            Name = "Alice Admin",
            Email = "admin@courtbook.dev",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@1234"),
            Phone = "01000000001",
            Role = Role.Admin
        };

        var owner = new User
        {
            Id = Guid.NewGuid(),
            Name = "Omar Owner",
            Email = "owner@courtbook.dev",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Owner@1234"),
            Phone = "01000000002",
            Role = Role.Owner
        };

        var client = new User
        {
            Id = Guid.NewGuid(),
            Name = "Clara Client",
            Email = "client@courtbook.dev",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Client@1234"),
            Phone = "01000000003",
            Role = Role.Client
        };

        await db.Users.AddRangeAsync(admin, owner, client);

        // ── Venue ─────────────────────────────────────────────────────────────

        var venue = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = owner.Id,
            Name = "City Sports Complex",
            City = "Cairo",
            Address = "12 Tahrir Square, Downtown Cairo"
        };

        await db.Venues.AddAsync(venue);

        // ── Courts ────────────────────────────────────────────────────────────

        var footballCourt = new Court
        {
            Id = Guid.NewGuid(),
            VenueId = venue.Id,
            Name = "Football Court A",
            SportType = SportType.Football,
            PricePerHour = 200.00m,
            IsActive = true
        };

        var padelCourt = new Court
        {
            Id = Guid.NewGuid(),
            VenueId = venue.Id,
            Name = "Padel Court 1",
            SportType = SportType.Padel,
            PricePerHour = 150.00m,
            IsActive = true
        };

        await db.Courts.AddRangeAsync(footballCourt, padelCourt);

        // ── Court Schedules ───────────────────────────────────────────────────
        // Both courts open Saturday–Thursday, 08:00–22:00
        // Friday is a day off (not seeded)

        var openDays = new[]
        {
            DayOfWeek.Saturday,
            DayOfWeek.Sunday,
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday
        };

        var open  = new TimeOnly(8, 0);   // 08:00
        var close = new TimeOnly(22, 0);  // 22:00

        var schedules = openDays
            .SelectMany(day => new[]
            {
                new CourtSchedule { Id = Guid.NewGuid(), CourtId = footballCourt.Id, DayOfWeek = day, OpenTime = open, CloseTime = close },
                new CourtSchedule { Id = Guid.NewGuid(), CourtId = padelCourt.Id,    DayOfWeek = day, OpenTime = open, CloseTime = close }
            });

        await db.CourtSchedules.AddRangeAsync(schedules);

        // ── Persist everything ────────────────────────────────────────────────

        await db.SaveChangesAsync();

        logger.LogInformation(
            "Seeding complete: 3 users, 1 venue, 2 courts, {count} schedules.",
            openDays.Length * 2);
    }
}

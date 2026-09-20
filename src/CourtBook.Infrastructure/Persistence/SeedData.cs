using BCrypt.Net;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CourtBook.Infrastructure.Persistence;

public static class SeedData
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();

        await db.Database.MigrateAsync();

        if (await db.Users.AnyAsync())
        {
            logger.LogInformation("Database already seeded — skipping.");
            return;
        }

        logger.LogInformation("Seeding rich Egyptian development data...");

        // ── Users ─────────────────────────────────────────────────────────────
        var admin = new User { Id = Guid.NewGuid(), Name = "Admin", Email = "admin@courtbook.eg", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"), Phone = "01000000001", Role = Role.Admin };
        var ahmed = new User { Id = Guid.NewGuid(), Name = "Ahmed Mostafa", Email = "ahmed.owner@courtbook.eg", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Owner@123"), Phone = "01000000002", Role = Role.Owner };
        var sara = new User { Id = Guid.NewGuid(), Name = "Sara Ibrahim", Email = "sara.owner@courtbook.eg", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Owner@123"), Phone = "01000000003", Role = Role.Owner };
        var mohamed = new User { Id = Guid.NewGuid(), Name = "محمد الحداد", Email = "mohamed.owner@courtbook.eg", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Owner@123"), Phone = "01000000007", Role = Role.Owner };
        
        var omar = new User { Id = Guid.NewGuid(), Name = "Omar Hassan", Email = "omar@gmail.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Client@123"), Phone = "01000000004", Role = Role.Client };
        var nada = new User { Id = Guid.NewGuid(), Name = "Nada Youssef", Email = "nada@gmail.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Client@123"), Phone = "01000000005", Role = Role.Client };
        var karim = new User { Id = Guid.NewGuid(), Name = "Karim Adel", Email = "karim@gmail.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Client@123"), Phone = "01000000006", Role = Role.Client };

        await db.Users.AddRangeAsync(admin, ahmed, sara, mohamed, omar, nada, karim);

        // ── Venues ─────────────────────────────────────────────────────────────
        var venues = new List<Venue>
        {
            new Venue { Id = Guid.NewGuid(), OwnerId = ahmed.Id, Name = "ملعب النجوم", City = "6th of October City", Address = "Al Mehwar, 6th of October" },
            new Venue { Id = Guid.NewGuid(), OwnerId = ahmed.Id, Name = "أكاديمية الرياضة", City = "Maadi, Cairo", Address = "Degla Square, Maadi" },
            new Venue { Id = Guid.NewGuid(), OwnerId = sara.Id, Name = "سنتر البطولة", City = "Nasr City, Cairo", Address = "Makram Ebeid St, Nasr City" },
            new Venue { Id = Guid.NewGuid(), OwnerId = sara.Id, Name = "ملاعب الزمالك الرياضية", City = "Zamalek, Cairo", Address = "Gezira Club St, Zamalek" },
            new Venue { Id = Guid.NewGuid(), OwnerId = ahmed.Id, Name = "نادي المستقبل", City = "New Cairo", Address = "90th Street, 5th Settlement" },
            new Venue { Id = Guid.NewGuid(), OwnerId = mohamed.Id, Name = "ملعب الشيخ سيد", City = "الفيوم", Address = "Elhawatem" }
        };

        await db.Venues.AddRangeAsync(venues);

        // ── Courts ────────────────────────────────────────────────────────────
        var courts = new List<Court>();
        var random = new Random(42);

        foreach (var v in venues)
        {
            // Add 2-3 courts per venue
            int courtCount = random.Next(2, 4);
            for (int i = 1; i <= courtCount; i++)
            {
                var sportTypes = new[] { SportType.Football, SportType.Padel, SportType.Tennis };
                var type = sportTypes[random.Next(sportTypes.Length)];
                
                decimal price = type switch
                {
                    SportType.Football => random.Next(150, 301), // 150-300
                    SportType.Padel => random.Next(200, 401), // 200-400
                    SportType.Tennis => random.Next(100, 251), // 100-250
                    _ => 200
                };
                
                // Round to nearest 10
                price = Math.Round(price / 10m) * 10;

                courts.Add(new Court
                {
                    Id = Guid.NewGuid(),
                    VenueId = v.Id,
                    Name = $"{type} Court {i}",
                    SportType = type,
                    PricePerHour = price,
                    IsActive = true
                });
            }
        }
        await db.Courts.AddRangeAsync(courts);

        // ── Schedules ─────────────────────────────────────────────────────────
        var schedules = new List<CourtSchedule>();
        
        var satThuDays = new[] { DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday };
        var satThuOpen = new TimeOnly(8, 0);
        var satThuClose = new TimeOnly(23, 0);
        
        var friOpen = new TimeOnly(14, 0);
        var friClose = new TimeOnly(23, 0);

        foreach (var c in courts)
        {
            foreach(var day in satThuDays)
            {
                schedules.Add(new CourtSchedule { Id = Guid.NewGuid(), CourtId = c.Id, DayOfWeek = day, OpenTime = satThuOpen, CloseTime = satThuClose });
            }
            schedules.Add(new CourtSchedule { Id = Guid.NewGuid(), CourtId = c.Id, DayOfWeek = DayOfWeek.Friday, OpenTime = friOpen, CloseTime = friClose });
        }
        await db.CourtSchedules.AddRangeAsync(schedules);

        // ── Bookings ──────────────────────────────────────────────────────────
        var bookings = new List<Booking>();
        var clients = new[] { omar, nada, karim };
        
        // 1 past booking, 4 future bookings
        var today = DateTime.UtcNow.Date;
        
        // Past Booking
        var court1 = courts[0];
        bookings.Add(new Booking
        {
            Id = Guid.NewGuid(), CourtId = court1.Id, UserId = omar.Id, TotalPrice = court1.PricePerHour,
            StartTime = today.AddDays(-2).AddHours(18), EndTime = today.AddDays(-2).AddHours(19)
        });
        
        // Future Bookings
        var court2 = courts[1];
        bookings.Add(new Booking
        {
            Id = Guid.NewGuid(), CourtId = court2.Id, UserId = nada.Id, TotalPrice = court2.PricePerHour * 2,
            StartTime = today.AddDays(1).AddHours(20), EndTime = today.AddDays(1).AddHours(22)
        });

        var court3 = courts[2];
        bookings.Add(new Booking
        {
            Id = Guid.NewGuid(), CourtId = court3.Id, UserId = karim.Id, TotalPrice = court3.PricePerHour,
            StartTime = today.AddDays(3).AddHours(16), EndTime = today.AddDays(3).AddHours(17)
        });
        
        var court4 = courts[3];
        bookings.Add(new Booking
        {
            Id = Guid.NewGuid(), CourtId = court4.Id, UserId = omar.Id, TotalPrice = court4.PricePerHour * 1.5m,
            StartTime = today.AddDays(5).AddHours(19), EndTime = today.AddDays(5).AddHours(20).AddMinutes(30)
        });
        
        var court5 = courts[4];
        bookings.Add(new Booking
        {
            Id = Guid.NewGuid(), CourtId = court5.Id, UserId = nada.Id, TotalPrice = court5.PricePerHour,
            StartTime = today.AddDays(7).AddHours(21), EndTime = today.AddDays(7).AddHours(22)
        });
        
        await db.Bookings.AddRangeAsync(bookings);

        // ── Persist ───────────────────────────────────────────────────────────
        await db.SaveChangesAsync();
        logger.LogInformation("Seeding complete: {c} venues, {co} courts, {b} bookings.", venues.Count, courts.Count, bookings.Count);
    }
}

using BCrypt.Net;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
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

        logger.LogInformation("Seeding rich PlaySpot development data...");

        // ── Users ─────────────────────────────────────────────────────────────
        var admin = new User { Id = Guid.NewGuid(), Name = "Admin", Email = "admin@courtbook.eg", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"), Phone = "01000000001", Role = Role.Admin };
        var ahmed = new User { Id = Guid.NewGuid(), Name = "Ahmed Mostafa", Email = "ahmed.owner@courtbook.eg", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Owner@123"), Phone = "01000000002", Role = Role.Owner };
        var sara = new User { Id = Guid.NewGuid(), Name = "Sara Ibrahim", Email = "sara.owner@courtbook.eg", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Owner@123"), Phone = "01000000003", Role = Role.Owner };
        var mohamed = new User { Id = Guid.NewGuid(), Name = "محمد الحداد", Email = "mohamed.owner@courtbook.eg", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Owner@123"), Phone = "01000000007", Role = Role.Owner };
        
        var omar = new User { Id = Guid.NewGuid(), Name = "Omar Hassan", Email = "omar@gmail.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Client@123"), Phone = "01000000004", Role = Role.Client };
        var nada = new User { Id = Guid.NewGuid(), Name = "Nada Youssef", Email = "nada@gmail.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Client@123"), Phone = "01000000005", Role = Role.Client };
        var karim = new User { Id = Guid.NewGuid(), Name = "Karim Adel", Email = "karim@gmail.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Client@123"), Phone = "01000000006", Role = Role.Client };

        await db.Users.AddRangeAsync(admin, ahmed, sara, mohamed, omar, nada, karim);

        // ── Amenities ─────────────────────────────────────────────────────────
        var parking = new Amenity { Id = Guid.NewGuid(), Name = "Free Parking", Icon = "bi-p-square", Category = "Comfort" };
        var showers = new Amenity { Id = Guid.NewGuid(), Name = "Showers & Lockers", Icon = "bi-droplet", Category = "Comfort" };
        var floodlights = new Amenity { Id = Guid.NewGuid(), Name = "Pro Floodlights", Icon = "bi-lightbulb", Category = "Facility" };
        var cafe = new Amenity { Id = Guid.NewGuid(), Name = "Sports Cafe & Lounge", Icon = "bi-cup-hot", Category = "Comfort" };
        var wifi = new Amenity { Id = Guid.NewGuid(), Name = "High-speed WiFi", Icon = "bi-wifi", Category = "Comfort" };
        var rental = new Amenity { Id = Guid.NewGuid(), Name = "Racket & Ball Rental", Icon = "bi-bag", Category = "Sport" };

        var allAmenities = new[] { parking, showers, floodlights, cafe, wifi, rental };
        await db.Amenities.AddRangeAsync(allAmenities);

        // ── Venues ────────────────────────────────────────────────────────────
        var venues = new List<Venue>
        {
            new Venue {
                Id = Guid.NewGuid(), OwnerId = ahmed.Id, Name = "ملعب النجوم", City = "6th of October City", Area = "Al Mehwar", Address = "Al Mehwar Central Axis, 6th of October",
                Description = "Premier sports hub with tournament-grade football pitches and glass padel courts. Equipped with full amenities and pro night lighting.",
                Phone = "01011112222", Email = "info@nogoomclub.eg", Latitude = 29.9737, Longitude = 30.9529, IsActive = true, IsVerified = true, AverageRating = 4.8, TotalReviews = 24
            },
            new Venue {
                Id = Guid.NewGuid(), OwnerId = ahmed.Id, Name = "أكاديمية الرياضة", City = "Maadi, Cairo", Area = "Degla", Address = "Degla Square, Street 218, Maadi",
                Description = "High-performance courts in the heart of Maadi. Features indoor padel courts and red-clay tennis courts.",
                Phone = "01022223333", Email = "contact@maadiacademy.eg", Latitude = 29.9602, Longitude = 31.2787, IsActive = true, IsVerified = true, AverageRating = 4.9, TotalReviews = 38
            },
            new Venue {
                Id = Guid.NewGuid(), OwnerId = sara.Id, Name = "سنتر البطولة", City = "Nasr City, Cairo", Area = "Makram Ebeid", Address = "Makram Ebeid St, Next to City Stars, Nasr City",
                Description = "Modern multi-sport facility featuring Olympic-sized 7-a-side and 5-a-side football turf with air-conditioned lounge.",
                Phone = "01033334444", Email = "info@elbotola.eg", Latitude = 30.0561, Longitude = 31.3438, IsActive = true, IsVerified = true, AverageRating = 4.7, TotalReviews = 19
            },
            new Venue {
                Id = Guid.NewGuid(), OwnerId = sara.Id, Name = "ملاعب الزمالك الرياضية", City = "Zamalek, Cairo", Area = "Gezira", Address = "Gezira Club St, Zamalek, Cairo",
                Description = "Iconic sports courts right by the Nile. Exclusive padel and tennis courts with premium European surfaces.",
                Phone = "01044445555", Email = "zamalekcourts@gmail.com", Latitude = 30.0618, Longitude = 31.2189, IsActive = true, IsVerified = true, AverageRating = 4.9, TotalReviews = 52
            },
            new Venue {
                Id = Guid.NewGuid(), OwnerId = ahmed.Id, Name = "نادي المستقبل", City = "New Cairo", Area = "5th Settlement", Address = "North 90th Street, 5th Settlement, New Cairo",
                Description = "State-of-the-art sports complex offering covered basketball courts, tennis courts, and world-class padel arenas.",
                Phone = "01055556666", Email = "mostakbal@newcairo.eg", Latitude = 30.0263, Longitude = 31.4913, IsActive = true, IsVerified = true, AverageRating = 4.6, TotalReviews = 15
            },
            new Venue {
                Id = Guid.NewGuid(), OwnerId = mohamed.Id, Name = "ملعب الشيخ سيد", City = "الفيوم", Area = "الحواتم", Address = "Elhawatem, Fayoum City",
                Description = "أحدث وأفضل ملاعب النجيل الصناعي والبادل في محافظة الفيوم. مجهز بأعلى مستويات الإضاءة وغرف تبديل الملابس وكافيه.",
                Phone = "01077778888", Email = "sheikhsayed@fayoum.eg", Latitude = 29.3084, Longitude = 30.8428, IsActive = true, IsVerified = true, AverageRating = 5.0, TotalReviews = 31
            }
        };

        await db.Venues.AddRangeAsync(venues);

        // ── Venue Amenities & Policies ────────────────────────────────────────
        var venueAmenities = new List<VenueAmenity>();
        var cancellationPolicies = new List<CancellationPolicy>();

        foreach (var v in venues)
        {
            // Attach 3-4 amenities per venue
            venueAmenities.Add(new VenueAmenity { VenueId = v.Id, AmenityId = parking.Id });
            venueAmenities.Add(new VenueAmenity { VenueId = v.Id, AmenityId = floodlights.Id });
            venueAmenities.Add(new VenueAmenity { VenueId = v.Id, AmenityId = showers.Id });
            venueAmenities.Add(new VenueAmenity { VenueId = v.Id, AmenityId = cafe.Id });

            cancellationPolicies.Add(new CancellationPolicy
            {
                Id = Guid.NewGuid(),
                VenueId = v.Id,
                FreeCancellationHours = 24,
                LateCancellationFeePercent = 50.0m,
                PolicyDescription = "Free cancellation up to 24 hours before game time. 50% fee applies afterwards."
            });
        }
        await db.VenueAmenities.AddRangeAsync(venueAmenities);
        await db.CancellationPolicies.AddRangeAsync(cancellationPolicies);

        // ── Courts ────────────────────────────────────────────────────────────
        var courts = new List<Court>();
        var random = new Random(42);

        foreach (var v in venues)
        {
            int courtCount = random.Next(2, 4);
            for (int i = 1; i <= courtCount; i++)
            {
                var sportTypes = new[] { SportType.Football, SportType.Padel, SportType.Tennis, SportType.Basketball };
                var type = sportTypes[random.Next(sportTypes.Length)];
                
                decimal price = type switch
                {
                    SportType.Football => random.Next(150, 301),
                    SportType.Padel => random.Next(200, 401),
                    SportType.Tennis => random.Next(100, 251),
                    SportType.Basketball => random.Next(120, 221),
                    _ => 200
                };
                price = Math.Round(price / 10m) * 10;

                var surface = type switch
                {
                    SportType.Football => "Artificial Turf (FIFA Certified)",
                    SportType.Padel => "Panoramic Glass & Textured Blue Turf",
                    SportType.Tennis => "Red Clay (Roland Garros Grade)",
                    SportType.Basketball => "Hardwood Parquet",
                    _ => "Standard"
                };

                courts.Add(new Court
                {
                    Id = Guid.NewGuid(),
                    VenueId = v.Id,
                    Name = $"{type} Court {i}",
                    Description = $"Official regulation size {type} court with professional lighting and high-grip surface.",
                    SportType = type,
                    PricePerHour = price,
                    SurfaceType = surface,
                    IsIndoor = i % 2 == 0,
                    Capacity = type == SportType.Football ? 12 : (type == SportType.Basketball ? 10 : 4),
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
            foreach (var day in satThuDays)
            {
                schedules.Add(new CourtSchedule { Id = Guid.NewGuid(), CourtId = c.Id, DayOfWeek = day, OpenTime = satThuOpen, CloseTime = satThuClose });
            }
            schedules.Add(new CourtSchedule { Id = Guid.NewGuid(), CourtId = c.Id, DayOfWeek = DayOfWeek.Friday, OpenTime = friOpen, CloseTime = friClose });
        }
        await db.CourtSchedules.AddRangeAsync(schedules);

        // ── Bookings ──────────────────────────────────────────────────────────
        var bookings = new List<Booking>();
        var today = DateTime.UtcNow.Date;
        
        // Past Completed Booking
        var court1 = courts[0];
        var pastBooking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = $"PS-{today.AddDays(-2):yyyyMMdd}-A1B2C3",
            CourtId = court1.Id,
            UserId = omar.Id,
            TotalPrice = court1.PricePerHour,
            Status = BookingStatus.Completed,
            PaymentStatus = PaymentStatus.Completed,
            StartTime = today.AddDays(-2).AddHours(18),
            EndTime = today.AddDays(-2).AddHours(19)
        };
        bookings.Add(pastBooking);
        
        // Future Confirmed Bookings
        var court2 = courts[1];
        bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = $"PS-{today.AddDays(1):yyyyMMdd}-D4E5F6",
            CourtId = court2.Id,
            UserId = nada.Id,
            TotalPrice = court2.PricePerHour * 2,
            Status = BookingStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            StartTime = today.AddDays(1).AddHours(20),
            EndTime = today.AddDays(1).AddHours(22)
        });

        var court3 = courts[2];
        bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = $"PS-{today.AddDays(3):yyyyMMdd}-G7H8J9",
            CourtId = court3.Id,
            UserId = karim.Id,
            TotalPrice = court3.PricePerHour,
            Status = BookingStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            StartTime = today.AddDays(3).AddHours(16),
            EndTime = today.AddDays(3).AddHours(17)
        });
        
        await db.Bookings.AddRangeAsync(bookings);

        // ── Review for completed booking ──────────────────────────────────────
        var review = new Review
        {
            Id = Guid.NewGuid(),
            BookingId = pastBooking.Id,
            UserId = omar.Id,
            VenueId = court1.VenueId,
            OverallRating = 5,
            CourtQualityRating = 5,
            CleanlinessRating = 5,
            StaffRating = 4,
            ValueRating = 5,
            Comment = "تجربة ممتازة جداً! أرضية الملعب والإضاءة على أعلى مستوى، وغرف الملابس نظيفة، والتعامل محترم وسريع.",
            OwnerResponse = "شكراً جزيلاً كابتن عمر! سعداء دائماً بوجودك معنا وفي انتظارك في مبارياتك القادمة ⚽",
            OwnerRespondedAt = DateTime.UtcNow.AddDays(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        await db.Reviews.AddAsync(review);

        // ── Sample Community Game ─────────────────────────────────────────────
        var openGame = new Game
        {
            Id = Guid.NewGuid(),
            Title = "مباراة كرة قدم ودية 6 ضد 6 - أكتوبر",
            SportType = SportType.Football,
            VenueId = venues[0].Id,
            CourtId = court1.Id,
            CreatorId = omar.Id,
            Date = DateOnly.FromDateTime(today.AddDays(2)),
            StartTime = new TimeOnly(19, 0),
            EndTime = new TimeOnly(20, 30),
            SkillLevel = SkillLevel.Intermediate,
            MinPlayers = 10,
            MaxPlayers = 12,
            PricePerPlayer = 35m,
            Status = GameStatus.Open,
            Description = "مباراة حماسية في ملعب النجوم بأكتوبر، محتاجين لاعبين وسط وهجوم. يرجى الحضور بزي رياضي قبل الموعد بـ 15 دقيقة."
        };
        await db.Games.AddAsync(openGame);

        await db.GameParticipants.AddAsync(new GameParticipant
        {
            Id = Guid.NewGuid(),
            GameId = openGame.Id,
            UserId = omar.Id,
            IsConfirmed = true,
            JoinedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        logger.LogInformation("Database seeded successfully with rich PlaySpot entities!");
    }
}

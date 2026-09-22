using BCrypt.Net;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CourtBook.Infrastructure.Persistence;

public static class SeedData
{
    public static async Task SeedAsync(IServiceProvider services, bool isDevelopment = true)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();

        // 1. System prerequisites: always seeded (even in Production)
        await EnsureSystemPrerequisitesAsync(db, logger, isDevelopment);

        // 2. Demo data: only seeded in Development
        if (isDevelopment)
        {
            await EnsureDemoDataAsync(db, logger);
        }
    }

    public static async Task EnsureSystemPrerequisitesAsync(AppDbContext db, ILogger logger, bool isDevelopment = true)
    {
        await db.Database.MigrateAsync();
        await EnsureTermsDocumentsAsync(db, logger);
        await EnsureBaseAmenitiesAsync(db, logger);
        await EnsureAdminAccountAsync(db, logger, isDevelopment);
        await EnsureHistoricalOwnerBalancesBackfilledAsync(db, logger);
    }

    public static async Task EnsureDemoDataAsync(AppDbContext db, ILogger logger)
    {
        await EnsureDemoUsersAndVenuesAsync(db, logger);
        await EnsureVenueAssetsAndDetailsAsync(db, logger);
        await EnsureCommunityGamesAsync(db, logger);
    }

    public static async Task EnsureTermsDocumentsAsync(AppDbContext db, ILogger logger)
    {
        if (!await db.TermsDocuments.AnyAsync())
        {
            logger.LogInformation("Seeding legal Terms Documents (Player & Facility Owner v1.0)...");

            var playerTerms = new TermsDocument
            {
                Id = Guid.NewGuid(),
                Type = TermsType.Player,
                Version = "1.0",
                Title = "PlaySpot Player Terms of Service & Code of Conduct",
                Content = @"PlaySpot Egypt — Player Terms of Service (Version 1.0)
Last Updated: September 2026

1. Account & Eligibility
By registering as a Player on PlaySpot, you confirm that you are at least 16 years of age and legally capable of entering into binding agreements in the Arab Republic of Egypt. You agree to provide accurate and truthful contact information.

2. Court Reservations & Commitments
- Bookings made through PlaySpot are legally binding match reservations with the respective sports facility.
- Players agree to arrive at least 10 minutes before the scheduled start time.
- Payment at venue must be settled prior to entering the court unless paid online.

3. Cancellation Policy & No-Show Penalties
- Players may cancel a reservation free of charge up to the facility's free cancellation window (typically 24 hours prior to match time).
- Late cancellations incur the designated late fee as specified in the booking confirmation.
- Repeated no-shows may lead to temporary or permanent suspension of your booking privileges.

4. Facility Safety & Conduct
- Players must adhere to all sportsmanship rules, safety instructions, and footwear regulations (e.g. non-marking shoes on indoor courts; turf shoes on artificial grass).
- PlaySpot is an intermediary discovery and reservation platform and is not liable for personal sports injuries or lost belongings on facility grounds.",
                PublishedAt = DateTime.UtcNow,
                IsActive = true
            };

            var ownerTerms = new TermsDocument
            {
                Id = Guid.NewGuid(),
                Type = TermsType.FacilityOwner,
                Version = "1.0",
                Title = "PlaySpot Facility Owner Requirements & Partnership Agreement",
                Content = @"PlaySpot Egypt — Facility Owner Requirements & Partnership Agreement (Version 1.0)
Last Updated: September 2026

1. Facility Listing & Verification
- Facility Owners must possess legal rights, operating licenses, or commercial authority to list, manage, and receive bookings for the sports complex.
- All newly registered facilities are placed in 'Pending Review' status until verified by PlaySpot administrators.

2. Court Quality & Safety Standards
- Facilities must maintain clean, safe, and playable court surfaces (e.g., FIFA-certified turf, shatterproof panoramic glass, regulation nets).
- Functioning floodlights, locker rooms, and safety equipment are required during all operating hours.

3. Schedule Accuracy & Double-Booking Prevention
- Facility Owners agree to maintain synchronized and accurate court schedules.
- PlaySpot's real-time reservation engine must be honored. Any unilateral cancellation by an owner of a confirmed player booking requires reasonable notice and administrative reporting.

4. Revenue, Commissions & Payouts
- Venue fees, peak hour adjustments, and cancellation terms must be clearly stated on the platform.
- PlaySpot settles verified reservation accounts in accordance with the signed commercial agreement.",
                PublishedAt = DateTime.UtcNow,
                IsActive = true
            };

            await db.TermsDocuments.AddRangeAsync(playerTerms, ownerTerms);
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded Terms Documents successfully.");
        }

        // Ensure terms acceptances for existing users without records
        var existingUsers = await db.Users.Include(u => u.TermsAcceptances).ToListAsync();
        var playerDoc = await db.TermsDocuments.FirstOrDefaultAsync(t => t.Type == TermsType.Player && t.IsActive);
        var ownerDoc = await db.TermsDocuments.FirstOrDefaultAsync(t => t.Type == TermsType.FacilityOwner && t.IsActive);

        var newAcceptances = new List<TermsAcceptance>();
        foreach (var u in existingUsers)
        {
            if (!u.TermsAcceptances.Any())
            {
                var doc = u.Role == Role.Owner ? ownerDoc : playerDoc;
                if (doc != null)
                {
                    newAcceptances.Add(new TermsAcceptance
                    {
                        Id = Guid.NewGuid(),
                        UserId = u.Id,
                        TermsDocumentId = doc.Id,
                        AcceptedAt = DateTime.UtcNow.AddMonths(-1)
                    });
                }
            }
        }

        if (newAcceptances.Any())
        {
            await db.TermsAcceptances.AddRangeAsync(newAcceptances);
            await db.SaveChangesAsync();
            logger.LogInformation("Created {Count} terms acceptance records for seeded users.", newAcceptances.Count);
        }
    }

    public static async Task EnsureAdminAccountAsync(AppDbContext db, ILogger logger, bool isDevelopment = true)
    {
        var admin = await db.Users.FirstOrDefaultAsync(u => u.Role == Role.Admin);
        if (admin == null)
        {
            var envPassword = Environment.GetEnvironmentVariable("ADMIN_INITIAL_PASSWORD") 
                              ?? Environment.GetEnvironmentVariable("Admin__InitialPassword");

            string initialPassword;
            if (!string.IsNullOrWhiteSpace(envPassword))
            {
                initialPassword = envPassword;
                logger.LogInformation("Creating System Administrator account using environment configuration password.");
            }
            else if (isDevelopment)
            {
                initialPassword = "Admin@123";
                logger.LogInformation("Seeding default System Administrator account for development...");
            }
            else
            {
                // In production/staging, never default to static known password
                initialPassword = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18));
                logger.LogWarning("SECURITY ALERT: No ADMIN_INITIAL_PASSWORD configured! Generated random one-time administrator password: {InitialPassword}. Please rotate immediately.", initialPassword);
            }

            var adminEmail = Environment.GetEnvironmentVariable("ADMIN_EMAIL") ?? "admin@courtbook.eg";

            admin = new User
            {
                Id = Guid.NewGuid(),
                Name = "PlaySpot Admin",
                Email = adminEmail,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(initialPassword),
                Phone = "01000000001",
                Role = Role.Admin,
                DateOfBirth = new DateOnly(1990, 1, 1),
                CreatedAt = DateTime.UtcNow
            };
            await db.Users.AddAsync(admin);
            await db.SaveChangesAsync();
            logger.LogInformation("System Administrator account created ({Email}).", admin.Email);
        }
    }

    public static async Task EnsureBaseAmenitiesAsync(AppDbContext db, ILogger logger)
    {
        var parking = await db.Amenities.FirstOrDefaultAsync(a => a.Name == "Free Parking") 
            ?? new Amenity { Id = Guid.NewGuid(), Name = "Free Parking", Icon = "bi-p-square", Category = "Comfort" };
        var showers = await db.Amenities.FirstOrDefaultAsync(a => a.Name == "Showers & Lockers") 
            ?? new Amenity { Id = Guid.NewGuid(), Name = "Showers & Lockers", Icon = "bi-droplet", Category = "Comfort" };
        var floodlights = await db.Amenities.FirstOrDefaultAsync(a => a.Name == "Pro Floodlights") 
            ?? new Amenity { Id = Guid.NewGuid(), Name = "Pro Floodlights", Icon = "bi-lightbulb", Category = "Facility" };
        var cafe = await db.Amenities.FirstOrDefaultAsync(a => a.Name == "Sports Cafe & Lounge") 
            ?? new Amenity { Id = Guid.NewGuid(), Name = "Sports Cafe & Lounge", Icon = "bi-cup-hot", Category = "Comfort" };
        var wifi = await db.Amenities.FirstOrDefaultAsync(a => a.Name == "High-speed WiFi") 
            ?? new Amenity { Id = Guid.NewGuid(), Name = "High-speed WiFi", Icon = "bi-wifi", Category = "Comfort" };
        var rental = await db.Amenities.FirstOrDefaultAsync(a => a.Name == "Racket & Ball Rental") 
            ?? new Amenity { Id = Guid.NewGuid(), Name = "Racket & Ball Rental", Icon = "bi-bag", Category = "Sport" };

        var baseAmenities = new[] { parking, showers, floodlights, cafe, wifi, rental };
        var added = false;
        foreach (var am in baseAmenities)
        {
            if (!await db.Amenities.AnyAsync(a => a.Name == am.Name || a.Id == am.Id))
            {
                await db.Amenities.AddAsync(am);
                added = true;
            }
        }
        if (added)
        {
            await db.SaveChangesAsync();
            logger.LogInformation("Base amenities seeded successfully.");
        }
    }

    public static Task EnsureUsersAndVenuesAsync(AppDbContext db, ILogger logger) => EnsureDemoUsersAndVenuesAsync(db, logger);

    public static async Task EnsureDemoUsersAndVenuesAsync(AppDbContext db, ILogger logger)
    {
        // ── 1. Admin & Demo Users ───────────────────────────────────────────
        await EnsureAdminAccountAsync(db, logger);
        var admin = await db.Users.FirstAsync(u => u.Role == Role.Admin);

        User? ahmed = await db.Users.FirstOrDefaultAsync(u => u.Email == "ahmed.owner@courtbook.eg");
        User? sara = await db.Users.FirstOrDefaultAsync(u => u.Email == "sara.owner@courtbook.eg");
        User? mohamed = await db.Users.FirstOrDefaultAsync(u => u.Email == "mohamed.owner@courtbook.eg");
        User? omar = await db.Users.FirstOrDefaultAsync(u => u.Email == "omar@gmail.com");
        User? nada = await db.Users.FirstOrDefaultAsync(u => u.Email == "nada@gmail.com");
        User? karim = await db.Users.FirstOrDefaultAsync(u => u.Email == "karim@gmail.com");

        if (ahmed == null)
        {
            ahmed = new User { Id = Guid.NewGuid(), Name = "Ahmed Mostafa", Email = "ahmed.owner@courtbook.eg", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Owner@123"), Phone = "01000000002", Role = Role.Owner, DateOfBirth = new DateOnly(1988, 1, 10) };
            sara = new User { Id = Guid.NewGuid(), Name = "Sara Ibrahim", Email = "sara.owner@courtbook.eg", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Owner@123"), Phone = "01000000003", Role = Role.Owner, DateOfBirth = new DateOnly(1992, 4, 12) };
            mohamed = new User { Id = Guid.NewGuid(), Name = "محمد الحداد", Email = "mohamed.owner@courtbook.eg", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Owner@123"), Phone = "01000000007", Role = Role.Owner, DateOfBirth = new DateOnly(1985, 11, 20) };

            omar = new User { Id = Guid.NewGuid(), Name = "Omar Hassan", Email = "omar@gmail.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Client@123"), Phone = "01000000004", Role = Role.Client, DateOfBirth = new DateOnly(1996, 5, 15) };
            nada = new User { Id = Guid.NewGuid(), Name = "Nada Youssef", Email = "nada@gmail.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Client@123"), Phone = "01000000005", Role = Role.Client, DateOfBirth = new DateOnly(2000, 8, 20) };
            karim = new User { Id = Guid.NewGuid(), Name = "Karim Adel", Email = "karim@gmail.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Client@123"), Phone = "01000000006", Role = Role.Client, DateOfBirth = new DateOnly(2009, 3, 10) };

            await db.Users.AddRangeAsync(ahmed, sara, mohamed, omar, nada, karim);
            await db.SaveChangesAsync();
        }
        else
        {
            bool updated = false;
            if (omar != null && !omar.DateOfBirth.HasValue) { omar.DateOfBirth = new DateOnly(1996, 5, 15); updated = true; }
            if (nada != null && !nada.DateOfBirth.HasValue) { nada.DateOfBirth = new DateOnly(2000, 8, 20); updated = true; }
            if (karim != null && !karim.DateOfBirth.HasValue) { karim.DateOfBirth = new DateOnly(2009, 3, 10); updated = true; }
            if (ahmed != null && !ahmed.DateOfBirth.HasValue) { ahmed.DateOfBirth = new DateOnly(1988, 1, 10); updated = true; }
            if (sara != null && !sara.DateOfBirth.HasValue) { sara.DateOfBirth = new DateOnly(1992, 4, 12); updated = true; }
            if (mohamed != null && !mohamed.DateOfBirth.HasValue) { mohamed.DateOfBirth = new DateOnly(1985, 11, 20); updated = true; }
            if (updated) await db.SaveChangesAsync();
        }

        // ── 2. Amenities ──────────────────────────────────────────────────────
        await EnsureBaseAmenitiesAsync(db, logger);
        var baseAmenities = await db.Amenities.ToListAsync();

        // ── 3. The 18 Sports Complexes ─────────────────────────────────────────
        var definitions = Get18ComplexDefinitions(ahmed!.Id, sara!.Id, mohamed!.Id, admin.Id);

        foreach (var def in definitions)
        {
            var existingVenue = await db.Venues
                .Include(v => v.Courts)
                .Include(v => v.Amenities)
                .Include(v => v.OperatingHours)
                .Include(v => v.CancellationPolicy)
                .FirstOrDefaultAsync(v => v.Name == def.Venue.Name);

            if (existingVenue is null)
            {
                logger.LogInformation("Adding complex: {Name} ({City}) [Status: {Status}]", def.Venue.Name, def.Venue.City, def.Venue.ApprovalStatus);
                
                var v = def.Venue;
                await db.Venues.AddAsync(v);

                // Add Amenities
                foreach (var am in baseAmenities.Take(4))
                {
                    db.VenueAmenities.Add(new VenueAmenity { VenueId = v.Id, AmenityId = am.Id });
                }

                // Add Policy
                db.CancellationPolicies.Add(new CancellationPolicy
                {
                    Id = Guid.NewGuid(),
                    VenueId = v.Id,
                    FreeCancellationHours = 24,
                    LateCancellationFeePercent = 50.0m,
                    PolicyDescription = "Free cancellation up to 24 hours before your match. 50% fee applies afterwards."
                });

                // Add 7-day Operating Hours
                for (int i = 0; i < 7; i++)
                {
                    var day = (DayOfWeek)i;
                    db.OperatingHours.Add(new OperatingHour
                    {
                        Id = Guid.NewGuid(),
                        VenueId = v.Id,
                        DayOfWeek = day,
                        OpenTime = day == DayOfWeek.Friday ? new TimeOnly(14, 0) : new TimeOnly(8, 0),
                        CloseTime = new TimeOnly(23, 0),
                        IsClosed = false
                    });
                }

                // Add Courts & Schedules
                foreach (var c in def.Courts)
                {
                    c.VenueId = v.Id;
                    await db.Courts.AddAsync(c);

                    // Add Court Schedules
                    for (int i = 0; i < 7; i++)
                    {
                        var day = (DayOfWeek)i;
                        db.CourtSchedules.Add(new CourtSchedule
                        {
                            Id = Guid.NewGuid(),
                            CourtId = c.Id,
                            DayOfWeek = day,
                            OpenTime = day == DayOfWeek.Friday ? new TimeOnly(14, 0) : new TimeOnly(8, 0),
                            CloseTime = new TimeOnly(23, 0)
                        });
                    }
                }

                await db.SaveChangesAsync();
            }
            else
            {
                // Ensure approval status is synchronized
                if (existingVenue.ApprovalStatus != def.Venue.ApprovalStatus)
                {
                    existingVenue.ApprovalStatus = def.Venue.ApprovalStatus;
                    existingVenue.RejectionReason = def.Venue.RejectionReason;
                    existingVenue.ApprovedAt = def.Venue.ApprovedAt;
                    existingVenue.ApprovedById = def.Venue.ApprovedById;
                }
            }
        }

        await db.SaveChangesAsync();
    }

    private static readonly Dictionary<string, (string PrimaryUrl, string PrimaryCaption, (string Url, string Caption)[] Gallery)> CuratedVenuePhotos = new()
    {
        ["ملعب النجوم"] = (
            "/images/venues/football/football-pitch-01.jpg",
            "ملعب النجوم - الملعب السباعي الرئيسي بنجيل صناعي معتمد وإضاءة ليلية",
            new[]
            {
                ("/images/venues/padel/padel-court-01.jpg", "ملعب بادل بانورامي بزجاج سيكوريت عالي المقاومة"),
                ("/images/venues/tennis/tennis-clay-01.jpg", "أرضية طفلة حمراء ممتازة مع صيانة دورية"),
                ("/images/venues/facilities/complex-exterior-01.jpg", "المدخل الرئيسي والمجمع الرياضي المتكامل"),
                ("/images/venues/facilities/lounge-interior-01.jpg", "استراحة اللاعبين ومقهى المجمع")
            }
        ),
        ["أكاديمية الرياضة"] = (
            "/images/venues/padel/padel-indoor-01.jpg",
            "أكاديمية الرياضة بالمعادي - ملاعب بادل مغطاة ومكيفة بالكامل",
            new[]
            {
                ("/images/venues/tennis/tennis-clay-02.jpg", "ملاعب التنس الترابية الأوروبية بدجلة المعادي"),
                ("/images/venues/football/football-exterior-01.jpg", "ملعب خماسي نجيل صناعي مريح للمفاصل"),
                ("/images/venues/facilities/lounge-interior-01.jpg", "استراحة VIP فندقية وغرف تبديل الملابس")
            }
        ),
        ["سنتر البطولة"] = (
            "/images/venues/football/football-pitch-02.jpg",
            "سنتر البطولة بمدينة نصر - ملعب كرة قدم أولمبي بنجيل صناعي حديث وإضاءة احترافية",
            new[]
            {
                ("/images/venues/basketball/basketball-indoor-02.jpg", "صالة كرة سلة مغطاة بأرضية خشبية باركيه"),
                ("/images/venues/football/football-detail-01.jpg", "ملعب كرة قدم خماسي مع شباك حماية كاملة"),
                ("/images/venues/facilities/complex-exterior-02.jpg", "واجهة صرح البطولة الرياضي قرب سيتي ستارز")
            }
        ),
        ["ملاعب الزمالك الرياضية"] = (
            "/images/venues/padel/padel-court-01.jpg",
            "ملاعب الزمالك الرياضية - ملاعب بادل بانورامية ساحرة على ضفاف النيل",
            new[]
            {
                ("/images/venues/tennis/tennis-grass-01.jpg", "ملعب تنس الجزيرة بإطلالة خضراء فريدة"),
                ("/images/venues/badminton/badminton-arena-01.jpg", "صالة تنس ريشة داخلية بمواصفات معتمدة"),
                ("/images/venues/facilities/complex-exterior-03.jpg", "حدائق المجمع والممرات النيلية بالزمالك")
            }
        ),
        ["نادي المستقبل"] = (
            "/images/venues/basketball/basketball-indoor-01.jpg",
            "نادي المستقبل بالتجمع الخامس - صالة كرة سلة مكيفة بأرضية باركيه للبطولات",
            new[]
            {
                ("/images/venues/volleyball/volleyball-indoor-01.jpg", "ملعب كرة طائرة مغطى بشباك أولمبية قابلة للضبط"),
                ("/images/venues/tennis/tennis-hard-02.jpg", "ملعب هارد كورت أكريليك سريع (US Open Style)"),
                ("/images/venues/facilities/complex-exterior-01.jpg", "المبنى الرياضي الرئيسي بشارع التسعين الشمالي")
            }
        ),
        ["ملعب الشيخ سيد"] = (
            "/images/venues/football/football-pitch-03.jpg",
            "ملعب الشيخ سيد بالفيوم - ملعب الأبطال الخماسي بنجيل صناعي فرنسي فاخر",
            new[]
            {
                ("/images/venues/padel/padel-court-05.jpg", "أول ملعب بادل زجاجي متكامل بمحافظة الفيوم"),
                ("/images/venues/football/football-pitch-05.jpg", "ملعب النجوم السباعي بكشافات إضاءة بيضاء ليلية"),
                ("/images/venues/facilities/lounge-interior-01.jpg", "كافيه واستراحة مكيفة لاستقبال الفرق واللاعبين")
            }
        ),
        ["أرينا التجمع الرياضي"] = (
            "/images/venues/padel/padel-court-02.jpg",
            "أرينا التجمع الرياضي - ملاعب بادل بانورامية بأرضية زرقاء حديثة",
            new[]
            {
                ("/images/venues/football/football-pitch-04.jpg", "ملاعب كرة قدم الجيل الرابع مع شاشات نتائج"),
                ("/images/venues/volleyball/volleyball-beach-01.jpg", "ملعب كرة طائرة خارجي بإضاءة كاشفة"),
                ("/images/venues/facilities/complex-exterior-02.jpg", "مبنى الأرينا العصري على محور السادات")
            }
        ),
        ["مجمع الأهرام الرياضي"] = (
            "/images/venues/facilities/complex-exterior-01.jpg",
            "مجمع الأهرام الرياضي - الصرح الرياضي الكبير بمنطقة الأهرام وفيصل بالجيزة",
            new[]
            {
                ("/images/venues/football/football-pitch-01.jpg", "ملاعب كرة قدم خماسية وسداسية نجيل صناعي ممتاز"),
                ("/images/venues/basketball/basketball-court-01.jpg", "صالة كرة سلة وكرة طائرة مجهزة بأعلى المعايير"),
                ("/images/venues/volleyball/volleyball-indoor-01.jpg", "صالة الكرة الطائرة والمدرجات"),
                ("/images/venues/facilities/lounge-interior-01.jpg", "استراحة وكافيه الزوار واللاعبين")
            }
        ),
        ["أكاديمية زايد الدولية"] = (
            "/images/venues/tennis/tennis-hard-01.jpg",
            "أكاديمية زايد الدولية - ملاعب تنس صلبة زرقاء احترافية ببيفرلي هيلز الشيخ زايد",
            new[]
            {
                ("/images/venues/padel/padel-court-03.jpg", "ملاعب بادل بانورامية زجاجية فاخرة بإضاءة LED"),
                ("/images/venues/football/football-pitch-04.jpg", "ملعب تدريب كرة القدم الخماسي للأكاديمية"),
                ("/images/venues/basketball/basketball-indoor-02.jpg", "ملعب كرة السلة بمواصفات الاتحاد الدولي"),
                ("/images/venues/facilities/lounge-interior-01.jpg", "لاونج الأكاديمية وخدمات استئجار المعدات")
            }
        ),
        ["نادي هليوبوليس سبورتس سنتر"] = (
            "/images/venues/tennis/tennis-clay-01.jpg",
            "نادي هليوبوليس بمصر الجديدة - ملاعب تنس أرضي ترابية عريقة بمواصفات رولان جاروس",
            new[]
            {
                ("/images/venues/padel/padel-detail-01.jpg", "ملاعب البادل المجهزة بأحدث الشباك والزجاج"),
                ("/images/venues/badminton/badminton-court-02.jpg", "صالة تنس الريشة المغطاة والمكيفة"),
                ("/images/venues/facilities/complex-exterior-03.jpg", "المدخل التاريخي والحدائق الرياضية لهليوبوليس")
            }
        ),
        ["نادي سموحة الأولمبي"] = (
            "/images/venues/football/football-pitch-04.jpg",
            "نادي سموحة الأولمبي بالإسكندرية - ملعب كرة قدم سباعي عريض بنجيل عالي الجودة ومدرجات",
            new[]
            {
                ("/images/venues/tennis/tennis-hard-02.jpg", "ملاعب التنس الأرضي في قلب سموحة"),
                ("/images/venues/volleyball/volleyball-indoor-01.jpg", "صالة مغطاة للكرة الطائرة بأرضية تارفلكس أولمبية"),
                ("/images/venues/facilities/lounge-interior-01.jpg", "غرف تبديل الملابس وخدمات الساونا والجاكوزي")
            }
        ),
        ["مجمع ستانلي للبادل والتنس"] = (
            "/images/venues/padel/padel-court-03.jpg",
            "مجمع ستانلي بالإسكندرية - ملاعب بادل زجاجية بانورامية بإطلالة ساحرة على البحر الأبيض المتوسط",
            new[]
            {
                ("/images/venues/tennis/tennis-hard-01.jpg", "ملعب تنس هارد كورت سريع بنسيم البحر"),
                ("/images/venues/badminton/badminton-arena-01.jpg", "صالة مغطاة لتنس الريشة بإضاءة مانعة للوهج"),
                ("/images/venues/facilities/complex-exterior-02.jpg", "تراس ومطعم المجمع المطل على كوبري ستانلي")
            }
        ),
        ["سبورتنج سنتر"] = (
            "/images/venues/volleyball/volleyball-indoor-01.jpg",
            "سبورتنج سنتر بالإسكندرية - صالة الكرة الطائرة الرسمية وتنس الريشة",
            new[]
            {
                ("/images/venues/basketball/basketball-court-01.jpg", "صالة كرة السلة الخشبية للمباريات التنافسية"),
                ("/images/venues/badminton/badminton-court-01.jpg", "ملعب ريشة طائرة مريح للركبتين"),
                ("/images/venues/facilities/lounge-interior-01.jpg", "استراحة متكاملة مع غرف ساونا وتبديل ملابس")
            }
        ),
        ["سنتر الدقي الرياضي"] = (
            "/images/venues/badminton/badminton-arena-01.jpg",
            "سنتر الدقي الرياضي - صالة تنس ريشة مكيفة وهادئة ومجهزة للمباريات الفردية والزوجية",
            new[]
            {
                ("/images/venues/football/football-pitch-02.jpg", "ملعب كرة قدم خماسي بموقع حيوي واستراتيجي وسط الدقي"),
                ("/images/venues/facilities/lounge-interior-01.jpg", "استقبال مكيف مع كافيه ومشروبات رياضية")
            }
        ),
        ["نادي شيراتون سبورت هاب"] = (
            "/images/venues/padel/padel-court-04.jpg",
            "نادي شيراتون سبورت هاب بمصر الجديدة - ملاعب بادل حديثة بأجواء راقية",
            new[]
            {
                ("/images/venues/basketball/basketball-indoor-01.jpg", "ملعب كرة سلة خارجي واسع"),
                ("/images/venues/volleyball/volleyball-beach-01.jpg", "ملعب كرة طائرة بمواصفات ممتازة"),
                ("/images/venues/facilities/complex-exterior-03.jpg", "واجهة النادي الراقية بمنطقة شيراتون هليوبوليس")
            }
        ),
        ["أكاديمية النيل بالمنصورة"] = (
            "/images/venues/tennis/tennis-clay-02.jpg",
            "أكاديمية النيل بالمنصورة - ملاعب تنس أرضي ترابية على ضفاف نيل الدلتا",
            new[]
            {
                ("/images/venues/football/football-pitch-05.jpg", "ملعب كرة قدم خماسي جديد بنجيل صناعي ممتاز"),
                ("/images/venues/facilities/complex-exterior-01.jpg", "مبنى الأكاديمية ومركز التدريب بالمنصورة")
            }
        ),
        ["مجمع القطامية رويال"] = (
            "/images/venues/facilities/complex-exterior-02.jpg",
            "مجمع القطامية رويال - المجمع الرياضي الفاخر بالتجمع والقاهرة الجديدة",
            new[]
            {
                ("/images/venues/padel/padel-court-05.jpg", "ملاعب بادل ملكية زجاجية بانورامية"),
                ("/images/venues/basketball/basketball-indoor-02.jpg", "صالة كرة سلة مغطاة بأرضية باركيه"),
                ("/images/venues/facilities/lounge-interior-01.jpg", "لاونج رويال الحصري وغرف استراحة كبار الشخصيات")
            }
        ),
        ["مجمع المهندسين كلوب"] = (
            "/images/venues/football/football-pitch-05.jpg",
            "مجمع المهندسين كلوب - ملعب كرة قدم خماسي في قلب الجيزة",
            new[]
            {
                ("/images/venues/football/football-detail-01.jpg", "أرضية نجيل صناعي محاطة بشباك حماية كاملة"),
                ("/images/venues/facilities/lounge-interior-01.jpg", "مكتب الحجوزات واستراحة اللاعبين")
            }
        )
    };

    public static async Task EnsureVenueAssetsAndDetailsAsync(AppDbContext db, ILogger logger)
    {
        var venues = await db.Venues
            .Include(v => v.Courts)
            .Include(v => v.Images)
            .Include(v => v.OperatingHours)
            .ToListAsync();

        if (!venues.Any()) return;

        foreach (var v in venues)
        {
            if (CuratedVenuePhotos.TryGetValue(v.Name, out var curated))
            {
                var currentPrimary = v.Images.FirstOrDefault(i => i.IsPrimary);
                bool needsUpdate = currentPrimary == null || currentPrimary.ImageUrl != curated.PrimaryUrl || v.Images.Count < curated.Gallery.Length + 1;

                if (needsUpdate)
                {
                    logger.LogInformation("Updating venue images for {Name} to curated unique photo set...", v.Name);

                    db.VenueImages.RemoveRange(v.Images);

                    int order = 1;
                    db.VenueImages.Add(new VenueImage
                    {
                        Id = Guid.NewGuid(),
                        VenueId = v.Id,
                        ImageUrl = curated.PrimaryUrl,
                        IsPrimary = true,
                        DisplayOrder = order++,
                        Caption = curated.PrimaryCaption
                    });

                    foreach (var (url, caption) in curated.Gallery)
                    {
                        db.VenueImages.Add(new VenueImage
                        {
                            Id = Guid.NewGuid(),
                            VenueId = v.Id,
                            ImageUrl = url,
                            IsPrimary = false,
                            DisplayOrder = order++,
                            Caption = caption
                        });
                    }
                }
            }
            else if (!v.Images.Any())
            {
                var sports = v.Courts.Select(c => c.SportType).Distinct().ToList();
                int order = 1;

                var primarySport = sports.FirstOrDefault();
                string primaryUrl = GetImageForSport(primarySport, true, v.Name.GetHashCode());

                db.VenueImages.Add(new VenueImage
                {
                    Id = Guid.NewGuid(),
                    VenueId = v.Id,
                    ImageUrl = primaryUrl,
                    IsPrimary = true,
                    DisplayOrder = order++,
                    Caption = $"{v.Name} - Main Facility & {primarySport} Arena"
                });

                foreach (var sp in sports.Skip(1))
                {
                    db.VenueImages.Add(new VenueImage
                    {
                        Id = Guid.NewGuid(),
                        VenueId = v.Id,
                        ImageUrl = GetImageForSport(sp, false, order),
                        IsPrimary = false,
                        DisplayOrder = order++,
                        Caption = $"{v.Name} - {sp} Court View"
                    });
                }

                db.VenueImages.Add(new VenueImage
                {
                    Id = Guid.NewGuid(),
                    VenueId = v.Id,
                    ImageUrl = "/images/venues/facilities/lounge-interior-01.jpg",
                    IsPrimary = false,
                    DisplayOrder = order++,
                    Caption = $"{v.Name} - Players Lounge & Amenities"
                });
            }
        }

        await db.SaveChangesAsync();
    }

    private static string GetImageForSport(SportType sport, bool isPrimary, int seed = 0)
    {
        var padelOptions = new[] { "/images/venues/padel/padel-court-01.jpg", "/images/venues/padel/padel-court-02.jpg", "/images/venues/padel/padel-court-03.jpg", "/images/venues/padel/padel-indoor-01.jpg" };
        var footballOptions = new[] { "/images/venues/football/football-pitch-01.jpg", "/images/venues/football/football-pitch-02.jpg", "/images/venues/football/football-pitch-03.jpg", "/images/venues/football/football-pitch-04.jpg" };
        var tennisOptions = new[] { "/images/venues/tennis/tennis-clay-01.jpg", "/images/venues/tennis/tennis-hard-01.jpg", "/images/venues/tennis/tennis-clay-02.jpg", "/images/venues/tennis/tennis-hard-02.jpg" };
        var basketballOptions = new[] { "/images/venues/basketball/basketball-indoor-01.jpg", "/images/venues/basketball/basketball-indoor-02.jpg", "/images/venues/basketball/basketball-court-01.jpg" };

        int idx = Math.Abs(seed);

        return sport switch
        {
            SportType.Football => isPrimary ? footballOptions[idx % footballOptions.Length] : "/images/venues/football/football-detail-01.jpg",
            SportType.Padel => isPrimary ? padelOptions[idx % padelOptions.Length] : "/images/venues/padel/padel-detail-01.jpg",
            SportType.Tennis => isPrimary ? tennisOptions[idx % tennisOptions.Length] : "/images/venues/tennis/tennis-grass-01.jpg",
            SportType.Basketball => isPrimary ? basketballOptions[idx % basketballOptions.Length] : "/images/venues/basketball/basketball-court-01.jpg",
            SportType.Volleyball => isPrimary ? "/images/venues/volleyball/volleyball-indoor-01.jpg" : "/images/venues/volleyball/volleyball-beach-01.jpg",
            SportType.Badminton => isPrimary ? "/images/venues/badminton/badminton-arena-01.jpg" : "/images/venues/badminton/badminton-court-01.jpg",
            _ => "/images/venues/facilities/complex-exterior-01.jpg"
        };
    }

    private class ComplexDefinition
    {
        public Venue Venue { get; set; } = null!;
        public List<Court> Courts { get; set; } = [];
    }

    private static List<ComplexDefinition> Get18ComplexDefinitions(Guid ahmedId, Guid saraId, Guid mohamedId, Guid adminId)
    {
        var list = new List<ComplexDefinition>();

        // 1. ملعب النجوم (6th of October) [Approved]
        var v1 = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = ahmedId,
            Name = "ملعب النجوم",
            City = "6th of October City",
            Area = "Al Mehwar",
            Address = "Al Mehwar Central Axis, 6th of October",
            Description = "مجمع رياضي متكامل يضم ملاعب كرة قدم معتمدة دولياً وملاعب بادل بانورامية وملاعب تنس ترابية مع إضاءة ليلية عالية الكفاءة وكافيه واستراحة مكيفة.",
            Phone = "01011112222",
            Email = "info@nogoomclub.eg",
            Latitude = 29.9737,
            Longitude = 30.9529,
            IsActive = true,
            IsVerified = true,
            ApprovalStatus = VenueApprovalStatus.Approved,
            ApprovedById = adminId,
            ApprovedAt = DateTime.UtcNow.AddDays(-60),
            AverageRating = 4.8,
            TotalReviews = 24
        };
        list.Add(new ComplexDefinition
        {
            Venue = v1,
            Courts = new List<Court>
            {
                new Court { Id = Guid.NewGuid(), Name = "Football Pitch 1 (7v7)", SportType = SportType.Football, PricePerHour = 300m, SurfaceType = "Artificial Turf (FIFA Certified)", Capacity = 14, IsIndoor = false, IsActive = true, Description = "ملعب سباعي رئيسي بنجيل صناعي فرنسي وإضاءة احترافية." },
                new Court { Id = Guid.NewGuid(), Name = "Football Pitch 2 (5v5)", SportType = SportType.Football, PricePerHour = 220m, SurfaceType = "Artificial Turf (FIFA Certified)", Capacity = 10, IsIndoor = false, IsActive = true, Description = "ملعب خماسي سريع مناسب للمباريات الحماسية والتحديات." },
                new Court { Id = Guid.NewGuid(), Name = "Padel Court 1 (Panoramic)", SportType = SportType.Padel, PricePerHour = 350m, SurfaceType = "Panoramic Glass & Textured Blue Turf", Capacity = 4, IsIndoor = false, IsActive = true, Description = "ملعب بادل بانورامي بزجاج سيكوريت عالي المقاومة وإضاءة LED." },
                new Court { Id = Guid.NewGuid(), Name = "Padel Court 2 (Glass)", SportType = SportType.Padel, PricePerHour = 320m, SurfaceType = "Panoramic Glass & Textured Blue Turf", Capacity = 4, IsIndoor = false, IsActive = true, Description = "ملعب بادل عصري محاط بزجاج مضاد للصدمات مع إمكانية استئجار المضارب." },
                new Court { Id = Guid.NewGuid(), Name = "Tennis Court 1 (Clay)", SportType = SportType.Tennis, PricePerHour = 200m, SurfaceType = "Red Clay (Roland Garros Grade)", Capacity = 4, IsIndoor = false, IsActive = true, Description = "أرضية طفلة حمراء ممتازة مع صيانة يومية وتخطيط احترافي." }
            }
        });

        // 2. أكاديمية الرياضة (Maadi) [Approved]
        var v2 = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = ahmedId,
            Name = "أكاديمية الرياضة",
            City = "Maadi, Cairo",
            Area = "Degla",
            Address = "Degla Square, Street 218, Maadi",
            Description = "أرقى ملاعب البادل والتنس وكرة القدم في قلب دجلة المعادي. مجمع هادئ ومجهز بغرف تبديل ملابس فندقية وكافيه راقي.",
            Phone = "01022223333",
            Email = "contact@maadiacademy.eg",
            Latitude = 29.9602,
            Longitude = 31.2787,
            IsActive = true,
            IsVerified = true,
            ApprovalStatus = VenueApprovalStatus.Approved,
            ApprovedById = adminId,
            ApprovedAt = DateTime.UtcNow.AddDays(-55),
            AverageRating = 4.9,
            TotalReviews = 38
        };
        list.Add(new ComplexDefinition
        {
            Venue = v2,
            Courts = new List<Court>
            {
                new Court { Id = Guid.NewGuid(), Name = "Indoor Padel 1", SportType = SportType.Padel, PricePerHour = 380m, SurfaceType = "Mondo Supercourt & Glass", Capacity = 4, IsIndoor = true, IsActive = true, Description = "ملعب بادل مغطى ومكيف بالكامل للعب في أي وقت من السنة." },
                new Court { Id = Guid.NewGuid(), Name = "Indoor Padel 2", SportType = SportType.Padel, PricePerHour = 380m, SurfaceType = "Mondo Supercourt & Glass", Capacity = 4, IsIndoor = true, IsActive = true, Description = "ملعب بادل داخلي مواصفات الجولة العالمية للمحترفين." },
                new Court { Id = Guid.NewGuid(), Name = "Clay Tennis Arena", SportType = SportType.Tennis, PricePerHour = 240m, SurfaceType = "European Red Clay", Capacity = 4, IsIndoor = false, IsActive = true, Description = "ملعب تنس أرضي ترابي بأعلى معايير الاتحاد الدولي." },
                new Court { Id = Guid.NewGuid(), Name = "Football Turf 1", SportType = SportType.Football, PricePerHour = 250m, SurfaceType = "Artificial Turf 50mm", Capacity = 10, IsIndoor = false, IsActive = true, Description = "ملعب نجيل صناعي خماسي ناعم ومريح للمفاصل." }
            }
        });

        // 3. سنتر البطولة (Nasr City) [Approved]
        var v3 = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = saraId,
            Name = "سنتر البطولة",
            City = "Nasr City, Cairo",
            Area = "Makram Ebeid",
            Address = "Makram Ebeid St, Next to City Stars, Nasr City",
            Description = "صرح رياضي متعدد الملاعب في مدينة نصر بالقرب من سيتي ستارز. ملاعب كرة قدم أولمبية وملاعب كرة سلة خشبية باركيه.",
            Phone = "01033334444",
            Email = "info@elbotola.eg",
            Latitude = 30.0561,
            Longitude = 31.3438,
            IsActive = true,
            IsVerified = true,
            ApprovalStatus = VenueApprovalStatus.Approved,
            ApprovedById = adminId,
            ApprovedAt = DateTime.UtcNow.AddDays(-50),
            AverageRating = 4.7,
            TotalReviews = 19
        };
        list.Add(new ComplexDefinition
        {
            Venue = v3,
            Courts = new List<Court>
            {
                new Court { Id = Guid.NewGuid(), Name = "Olympic Football Pitch", SportType = SportType.Football, PricePerHour = 280m, SurfaceType = "FIFA Certified Artificial Turf", Capacity = 14, IsIndoor = false, IsActive = true, Description = "ملعب سباعي واسع مع مدرجات ومسار إحماء جانبي." },
                new Court { Id = Guid.NewGuid(), Name = "Mini Football Pitch", SportType = SportType.Football, PricePerHour = 180m, SurfaceType = "Artificial Turf", Capacity = 10, IsIndoor = false, IsActive = true, Description = "ملعب خماسي محاط بشباك حماية كاملة وإضاءة بيضاء." },
                new Court { Id = Guid.NewGuid(), Name = "Indoor Basketball Hall", SportType = SportType.Basketball, PricePerHour = 220m, SurfaceType = "Hardwood Parquet Floor", Capacity = 10, IsIndoor = true, IsActive = true, Description = "صالة مغطاة بأرضية باركيه أصلية وأبراج سلة احترافية قابلة للضبط." }
            }
        });

        // 4. ملاعب الزمالك الرياضية (Zamalek) [Approved]
        var v4 = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = saraId,
            Name = "ملاعب الزمالك الرياضية",
            City = "Zamalek, Cairo",
            Area = "Gezira",
            Address = "Gezira Club St, Zamalek, Cairo",
            Description = "ملاعب رياضية ساحرة مطلة على النيل في جزيرة الزمالك. بادل بانورامي، تنس، وملاعب تنس ريشة طائرة في أجواء راقية.",
            Phone = "01044445555",
            Email = "zamalekcourts@gmail.com",
            Latitude = 30.0618,
            Longitude = 31.2189,
            IsActive = true,
            IsVerified = true,
            ApprovalStatus = VenueApprovalStatus.Approved,
            ApprovedById = adminId,
            ApprovedAt = DateTime.UtcNow.AddDays(-45),
            AverageRating = 4.9,
            TotalReviews = 52
        };
        list.Add(new ComplexDefinition
        {
            Venue = v4,
            Courts = new List<Court>
            {
                new Court { Id = Guid.NewGuid(), Name = "Nile Padel Court 1", SportType = SportType.Padel, PricePerHour = 400m, SurfaceType = "Full Panoramic Glass & Turf", Capacity = 4, IsIndoor = false, IsActive = true, Description = "ملعب بادل بزاوية رؤية بانورامية كاملة على ضفاف نيل الزمالك." },
                new Court { Id = Guid.NewGuid(), Name = "Nile Padel Court 2", SportType = SportType.Padel, PricePerHour = 360m, SurfaceType = "Tempered Glass & Textured Turf", Capacity = 4, IsIndoor = false, IsActive = true, Description = "ملعب بادل حديث مزود بنظام تبريد رذاذي ومقاعد استراحة." },
                new Court { Id = Guid.NewGuid(), Name = "Gezira Tennis Court", SportType = SportType.Tennis, PricePerHour = 250m, SurfaceType = "French Clay Surface", Capacity = 4, IsIndoor = false, IsActive = true, Description = "ملعب تنس أرضي تاريخي بحالة ممتازة وإطلالة خضراء." },
                new Court { Id = Guid.NewGuid(), Name = "Badminton Arena", SportType = SportType.Badminton, PricePerHour = 150m, SurfaceType = "Indoor Taraflex Sport Flooring", Capacity = 4, IsIndoor = true, IsActive = true, Description = "ملعب تنس ريشة داخلي بمواصفات معتمدة وخطوط رسمية." }
            }
        });

        // 5. نادي المستقبل (New Cairo) [Approved]
        var v5 = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = ahmedId,
            Name = "نادي المستقبل",
            City = "New Cairo",
            Area = "5th Settlement",
            Address = "North 90th Street, 5th Settlement, New Cairo",
            Description = "مجمع رياضي عملاق بشارع التسعين الشمالي. يشمل صالات كرة السلة، ملاعب الكرة الطائرة الشاطئية والمغطاة، وملاعب البادل والتنس.",
            Phone = "01055556666",
            Email = "mostakbal@newcairo.eg",
            Latitude = 30.0263,
            Longitude = 31.4913,
            IsActive = true,
            IsVerified = true,
            ApprovalStatus = VenueApprovalStatus.Approved,
            ApprovedById = adminId,
            ApprovedAt = DateTime.UtcNow.AddDays(-40),
            AverageRating = 4.6,
            TotalReviews = 15
        };
        list.Add(new ComplexDefinition
        {
            Venue = v5,
            Courts = new List<Court>
            {
                new Court { Id = Guid.NewGuid(), Name = "Pro Basketball Court", SportType = SportType.Basketball, PricePerHour = 230m, SurfaceType = "Hardwood Parquet", Capacity = 10, IsIndoor = true, IsActive = true, Description = "صالة باركيه مكيفة للبطولات والمباريات الحماسية." },
                new Court { Id = Guid.NewGuid(), Name = "Indoor Volleyball Hall", SportType = SportType.Volleyball, PricePerHour = 190m, SurfaceType = "Taraflex Multi-Sport Mat", Capacity = 12, IsIndoor = true, IsActive = true, Description = "ملعب كرة طائرة مغطى بشباك أولمبية قابلة لتعديل الارتفاع." },
                new Court { Id = Guid.NewGuid(), Name = "Hard Court Tennis", SportType = SportType.Tennis, PricePerHour = 210m, SurfaceType = "Acrylic Hardcourt (US Open Style)", Capacity = 4, IsIndoor = false, IsActive = true, Description = "ملعب هارد كورت سريع مناسب للإرسالات القوية واللعب الهجومي." }
            }
        });

        // 6. مجمع الشيخ سيد الرياضي (Fayoum) [Approved]
        var v6 = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = mohamedId,
            Name = "ملعب الشيخ سيد",
            City = "الفيوم",
            Area = "الحواتم",
            Address = "Elhawatem, Fayoum City",
            Description = "أكبر وأحدث مجمع نجيل صناعي وبادل في محافظة الفيوم. مجهز بأعلى مستويات الإضاءة والكافيهات وغرف الملابس واستراحة خاصة.",
            Phone = "01077778888",
            Email = "sheikhsayed@fayoum.eg",
            Latitude = 29.3084,
            Longitude = 30.8428,
            IsActive = true,
            IsVerified = true,
            ApprovalStatus = VenueApprovalStatus.Approved,
            ApprovedById = adminId,
            ApprovedAt = DateTime.UtcNow.AddDays(-35),
            AverageRating = 5.0,
            TotalReviews = 31
        };
        list.Add(new ComplexDefinition
        {
            Venue = v6,
            Courts = new List<Court>
            {
                new Court { Id = Guid.NewGuid(), Name = "ملعب الأبطال الخماسي", SportType = SportType.Football, PricePerHour = 180m, SurfaceType = "Artificial Turf (FIFA Certified)", Capacity = 10, IsIndoor = false, IsActive = true, Description = "أفضل أرضية نجيل في الفيوم بدون مطبات مع شباك حديثة." },
                new Court { Id = Guid.NewGuid(), Name = "ملعب النجوم السباعي", SportType = SportType.Football, PricePerHour = 260m, SurfaceType = "Artificial Turf (FIFA Certified)", Capacity = 14, IsIndoor = false, IsActive = true, Description = "ملعب سباعي بمساحة قانونية كاملة للبطولات والمجموعات الكبيرة." },
                new Court { Id = Guid.NewGuid(), Name = "ملعب بادل الفيوم الأول", SportType = SportType.Padel, PricePerHour = 280m, SurfaceType = "Panoramic Glass & Textured Turf", Capacity = 4, IsIndoor = false, IsActive = true, Description = "أول ملعب بادل زجاجي متكامل بمحافظة الفيوم." }
            }
        });

        // 7. أرينا التجمع الرياضي (New Cairo) [Approved]
        var v7 = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = ahmedId,
            Name = "أرينا التجمع الرياضي",
            City = "New Cairo",
            Area = "1st Settlement",
            Address = "Al Sadat Axis, 1st Settlement, New Cairo",
            Description = "مجمع عصري يضم 3 ملاعب بادل مميزة وملعبين كرة قدم من الجيل الرابع وملعب كرة طائرة متكامل.",
            Phone = "01088889999",
            Email = "tagamoa.arena@courtbook.eg",
            Latitude = 30.0450,
            Longitude = 31.4700,
            IsActive = true,
            IsVerified = true,
            ApprovalStatus = VenueApprovalStatus.Approved,
            ApprovedById = adminId,
            ApprovedAt = DateTime.UtcNow.AddDays(-30),
            AverageRating = 4.8,
            TotalReviews = 27
        };
        list.Add(new ComplexDefinition
        {
            Venue = v7,
            Courts = new List<Court>
            {
                new Court { Id = Guid.NewGuid(), Name = "Arena Football Turf 1", SportType = SportType.Football, PricePerHour = 270m, SurfaceType = "Artificial Turf 50mm", Capacity = 12, IsIndoor = false, IsActive = true, Description = "ملعب كرة قدم سداسي مع شاشات نتائج إلكترونية." },
                new Court { Id = Guid.NewGuid(), Name = "Arena Football Turf 2", SportType = SportType.Football, PricePerHour = 250m, SurfaceType = "Artificial Turf 50mm", Capacity = 10, IsIndoor = false, IsActive = true, Description = "ملعب خماسي مجهز بالكامل." },
                new Court { Id = Guid.NewGuid(), Name = "Arena Padel Blue 1", SportType = SportType.Padel, PricePerHour = 340m, SurfaceType = "Panoramic Blue Turf", Capacity = 4, IsIndoor = false, IsActive = true, Description = "ملعب بادل حديث بأرضية زرقاء مريحة." },
                new Court { Id = Guid.NewGuid(), Name = "Arena Padel Blue 2", SportType = SportType.Padel, PricePerHour = 340m, SurfaceType = "Panoramic Blue Turf", Capacity = 4, IsIndoor = false, IsActive = true, Description = "ملعب بادل بانورامي ثانٍ مناسب للتدريبات والبطولات." },
                new Court { Id = Guid.NewGuid(), Name = "Volleyball Court", SportType = SportType.Volleyball, PricePerHour = 180m, SurfaceType = "Cushioned Court", Capacity = 12, IsIndoor = false, IsActive = true, Description = "ملعب كرة طائرة خارجي مضاء بأفضل كشافات." }
            }
        });

        // 8. مجمع الأهرام الرياضي (Haram / Giza) [Approved]
        var v8 = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = saraId,
            Name = "مجمع الأهرام الرياضي",
            City = "Giza",
            Area = "Haram",
            Address = "Faisal & Haram Junction, Giza",
            Description = "صرح رياضي شهير بالجيزة يقدم ملاعب كرة قدم خماسية وسداسية وصالة كرة سلة وكرة طائرة مجهزة بأعلى مستوى.",
            Phone = "01099990001",
            Email = "ahram.sports@courtbook.eg",
            Latitude = 29.9950,
            Longitude = 31.1800,
            IsActive = true,
            IsVerified = true,
            ApprovalStatus = VenueApprovalStatus.Approved,
            ApprovedById = adminId,
            ApprovedAt = DateTime.UtcNow.AddDays(-28),
            AverageRating = 4.7,
            TotalReviews = 18
        };
        list.Add(new ComplexDefinition
        {
            Venue = v8,
            Courts = new List<Court>
            {
                new Court { Id = Guid.NewGuid(), Name = "ملعب الأهرام الأول", SportType = SportType.Football, PricePerHour = 200m, SurfaceType = "Synthetic Turf", Capacity = 10, IsIndoor = false, IsActive = true, Description = "ملعب خماسي بإضاءة ممتازة وغرف ملابس نظيفة." },
                new Court { Id = Guid.NewGuid(), Name = "Basketball Parquet", SportType = SportType.Basketball, PricePerHour = 190m, SurfaceType = "Wooden Parquet", Capacity = 10, IsIndoor = true, IsActive = true, Description = "صالة كرة سلة خشبية مغطاة ومحمية." },
                new Court { Id = Guid.NewGuid(), Name = "Volleyball Arena", SportType = SportType.Volleyball, PricePerHour = 160m, SurfaceType = "Multi-Use Floor", Capacity = 12, IsIndoor = true, IsActive = true, Description = "ملعب كرة طائرة مع شباك رسمية وحكام حسب الطلب." }
            }
        });

        // 9. نادي هليوبوليس سبورتس سنتر (Heliopolis) [Approved]
        var v9 = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = saraId,
            Name = "نادي هليوبوليس سبورتس سنتر",
            City = "Heliopolis",
            Area = "Korba",
            Address = "Baghdad St, Korba, Heliopolis, Cairo",
            Description = "أرقى ملاعب التنس الأرضي والبادل وتنس الريشة في الكوربة بمصر الجديدة. بيئة راقية وتاريخ عريق وخدمات متميزة.",
            Phone = "01099990002",
            Email = "heliopolis.center@courtbook.eg",
            Latitude = 30.0890,
            Longitude = 31.3280,
            IsActive = true,
            IsVerified = true,
            ApprovalStatus = VenueApprovalStatus.Approved,
            ApprovedById = adminId,
            ApprovedAt = DateTime.UtcNow.AddDays(-25),
            AverageRating = 4.9,
            TotalReviews = 41
        };
        list.Add(new ComplexDefinition
        {
            Venue = v9,
            Courts = new List<Court>
            {
                new Court { Id = Guid.NewGuid(), Name = "Center Court (Clay)", SportType = SportType.Tennis, PricePerHour = 260m, SurfaceType = "Red Clay", Capacity = 4, IsIndoor = false, IsActive = true, Description = "الملعب الرئيسي بنادي هليوبوليس ترابي أصلي." },
                new Court { Id = Guid.NewGuid(), Name = "Hard Court 1", SportType = SportType.Tennis, PricePerHour = 220m, SurfaceType = "Acrylic Hardcourt", Capacity = 4, IsIndoor = false, IsActive = true, Description = "ملعب تنس هارد كورت مضاد للانزلاق." },
                new Court { Id = Guid.NewGuid(), Name = "Heliopolis Padel", SportType = SportType.Padel, PricePerHour = 370m, SurfaceType = "Panoramic Glass", Capacity = 4, IsIndoor = false, IsActive = true, Description = "ملعب بادل زجاجي محاط بأشجار الكوربة العريقة." },
                new Court { Id = Guid.NewGuid(), Name = "Badminton Hall", SportType = SportType.Badminton, PricePerHour = 160m, SurfaceType = "Wooden Court", Capacity = 4, IsIndoor = true, IsActive = true, Description = "صالة تنس ريشة مكيفة مجهزة بشباك بطولات." }
            }
        });

        // 10. أكاديمية زايد الدولية (Sheikh Zayed) [Approved]
        var v10 = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = ahmedId,
            Name = "أكاديمية زايد الدولية",
            City = "Sheikh Zayed",
            Area = "Al Bustan",
            Address = "Al Bustan District, Near Arkan Plaza, Sheikh Zayed City",
            Description = "مجمع متطور عالمي في قلب الشيخ زايد يضم 4 ملاعب بادل زجاجية، ملعب تنس، ملعب كرة قدم سداسي، وملعب كرة سلة مكفر.",
            Phone = "01099990003",
            Email = "zayed.academy@courtbook.eg",
            Latitude = 30.0150,
            Longitude = 30.9850,
            IsActive = true,
            IsVerified = true,
            ApprovalStatus = VenueApprovalStatus.Approved,
            ApprovedById = adminId,
            ApprovedAt = DateTime.UtcNow.AddDays(-22),
            AverageRating = 4.9,
            TotalReviews = 49
        };
        list.Add(new ComplexDefinition
        {
            Venue = v10,
            Courts = new List<Court>
            {
                new Court { Id = Guid.NewGuid(), Name = "Zayed Padel 1", SportType = SportType.Padel, PricePerHour = 390m, SurfaceType = "World Padel Tour Glass", Capacity = 4, IsIndoor = false, IsActive = true, Description = "ملعب بادل رسمي بزجاج بانورامي كامل." },
                new Court { Id = Guid.NewGuid(), Name = "Zayed Padel 2", SportType = SportType.Padel, PricePerHour = 390m, SurfaceType = "World Padel Tour Glass", Capacity = 4, IsIndoor = false, IsActive = true, Description = "ملعب بادل مجهز بكاميرات تسجيل المباريات حسب الطلب." },
                new Court { Id = Guid.NewGuid(), Name = "Tennis Court (Hard)", SportType = SportType.Tennis, PricePerHour = 230m, SurfaceType = "US Open Surface", Capacity = 4, IsIndoor = false, IsActive = true, Description = "ملعب تنس صلب ممتاز للتمارين والمباريات الودية." },
                new Court { Id = Guid.NewGuid(), Name = "Zayed Football Arena", SportType = SportType.Football, PricePerHour = 290m, SurfaceType = "FIFA Certified Turf", Capacity = 12, IsIndoor = false, IsActive = true, Description = "ملعب كرة قدم سداسي مع إضاءة بروجكتور LED." },
                new Court { Id = Guid.NewGuid(), Name = "Basketball Arena", SportType = SportType.Basketball, PricePerHour = 220m, SurfaceType = "Cushioned Hardcourt", Capacity = 10, IsIndoor = false, IsActive = true, Description = "ملعب كرة سلة بإرتفاع حلقة قانوني وتخطيط دقيق." }
            }
        });

        // 11. نادي سموحة الأولمبي (Alexandria) [Approved]
        var v11 = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = saraId,
            Name = "نادي سموحة الأولمبي",
            City = "Alexandria",
            Area = "Smouha",
            Address = "Victor Emmanuel Square, Smouha, Alexandria",
            Description = "منشأة رياضية عريقة في سموحة بالإسكندرية تضم ملاعب كرة قدم كبرى، ملاعب تنس ترابية، وصالة كرة طائرة أولمبية.",
            Phone = "01099990004",
            Email = "smouha.facility@courtbook.eg",
            Latitude = 31.2150,
            Longitude = 29.9550,
            IsActive = true,
            IsVerified = true,
            ApprovalStatus = VenueApprovalStatus.Approved,
            ApprovedById = adminId,
            ApprovedAt = DateTime.UtcNow.AddDays(-20),
            AverageRating = 4.8,
            TotalReviews = 35
        };
        list.Add(new ComplexDefinition
        {
            Venue = v11,
            Courts = new List<Court>
            {
                new Court { Id = Guid.NewGuid(), Name = "Smouha Football Pitch", SportType = SportType.Football, PricePerHour = 260m, SurfaceType = "High Quality Turf", Capacity = 14, IsIndoor = false, IsActive = true, Description = "ملعب كرة قدم سباعي عريض ومناسب للمجموعات الكبيرة." },
                new Court { Id = Guid.NewGuid(), Name = "Alex Clay Tennis 1", SportType = SportType.Tennis, PricePerHour = 210m, SurfaceType = "Red Clay", Capacity = 4, IsIndoor = false, IsActive = true, Description = "ملعب تنس ترابي مع صيانة ممتازة للري ودك الأرضية." },
                new Court { Id = Guid.NewGuid(), Name = "Smouha Volleyball Hall", SportType = SportType.Volleyball, PricePerHour = 170m, SurfaceType = "Taraflex Mat", Capacity = 12, IsIndoor = true, IsActive = true, Description = "صالة مغطاة للكرة الطائرة بأعلى المعايير." }
            }
        });

        // 12. مجمع ستانلي للبادل والتنس (Alexandria) [Approved]
        var v12 = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = saraId,
            Name = "مجمع ستانلي للبادل والتنس",
            City = "Alexandria",
            Area = "Stanley",
            Address = "Corniche Road, Beside Stanley Bridge, Alexandria",
            Description = "ملاعب بادل وتنس وتنس ريشة مميزة بإطلالة بحرية ساحرة بجوار كوبري ستانلي عروس البحر المتوسط.",
            Phone = "01099990005",
            Email = "stanley.padel@courtbook.eg",
            Latitude = 31.2350,
            Longitude = 29.9500,
            IsActive = true,
            IsVerified = true,
            ApprovalStatus = VenueApprovalStatus.Approved,
            ApprovedById = adminId,
            ApprovedAt = DateTime.UtcNow.AddDays(-18),
            AverageRating = 4.9,
            TotalReviews = 44
        };
        list.Add(new ComplexDefinition
        {
            Venue = v12,
            Courts = new List<Court>
            {
                new Court { Id = Guid.NewGuid(), Name = "Sea View Padel 1", SportType = SportType.Padel, PricePerHour = 370m, SurfaceType = "Sea View Panoramic Glass", Capacity = 4, IsIndoor = false, IsActive = true, Description = "ملعب بادل بإطلالة بحرية رائعة ونسيم البحر الأبيض المتوسط." },
                new Court { Id = Guid.NewGuid(), Name = "Sea View Padel 2", SportType = SportType.Padel, PricePerHour = 370m, SurfaceType = "Sea View Panoramic Glass", Capacity = 4, IsIndoor = false, IsActive = true, Description = "ملعب بادل بانورامي مجهز للمباريات المسائية." },
                new Court { Id = Guid.NewGuid(), Name = "Stanley Hard Tennis", SportType = SportType.Tennis, PricePerHour = 230m, SurfaceType = "Hardcourt", Capacity = 4, IsIndoor = false, IsActive = true, Description = "ملعب تنس صلب سريع." },
                new Court { Id = Guid.NewGuid(), Name = "Stanley Badminton Hall", SportType = SportType.Badminton, PricePerHour = 150m, SurfaceType = "Indoor Taraflex", Capacity = 4, IsIndoor = true, IsActive = true, Description = "صالة مغطاة لتنس الريشة مع إضاءة مانعة للوهج." }
            }
        });

        // 13. سبورتنج سنتر (Alexandria) [Approved]
        var v13 = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = ahmedId,
            Name = "سبورتنج سنتر",
            City = "Alexandria",
            Area = "Sporting",
            Address = "Sporting Club Axis, Alexandria",
            Description = "صالة رياضية متكاملة تضم ملاعب كرة السلة المغطاة، الكرة الطائرة، وتنس الريشة مع غرف ساونا وتبديل ملابس.",
            Phone = "01099990006",
            Email = "sporting.alex@courtbook.eg",
            Latitude = 31.2180,
            Longitude = 29.9350,
            IsActive = true,
            IsVerified = true,
            ApprovalStatus = VenueApprovalStatus.Approved,
            ApprovedById = adminId,
            ApprovedAt = DateTime.UtcNow.AddDays(-15),
            AverageRating = 4.7,
            TotalReviews = 22
        };
        list.Add(new ComplexDefinition
        {
            Venue = v13,
            Courts = new List<Court>
            {
                new Court { Id = Guid.NewGuid(), Name = "Alex Basketball Hall", SportType = SportType.Basketball, PricePerHour = 210m, SurfaceType = "Maple Wood Parquet", Capacity = 10, IsIndoor = true, IsActive = true, Description = "صالة خشبية للمباريات التنافسية وبطولات السلة." },
                new Court { Id = Guid.NewGuid(), Name = "Volleyball Court 1", SportType = SportType.Volleyball, PricePerHour = 170m, SurfaceType = "Indoor Floor", Capacity = 12, IsIndoor = true, IsActive = true, Description = "ملعب كرة طائرة احترافي." },
                new Court { Id = Guid.NewGuid(), Name = "Badminton Court 1", SportType = SportType.Badminton, PricePerHour = 140m, SurfaceType = "Indoor Mat", Capacity = 4, IsIndoor = true, IsActive = true, Description = "ملعب ريشة طائرة مريح للركبتين." }
            }
        });

        // 14. سنتر الدقي الرياضي (Dokki / Giza) [Approved]
        var v14 = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = mohamedId,
            Name = "سنتر الدقي الرياضي",
            City = "Giza",
            Area = "Dokki",
            Address = "Mossadak St, Dokki, Giza",
            Description = "موقع استراتيجي بالدقي يضم ملعب كرة قدم نجيل صناعي متميز وصالة تنس ريشة مكيفة بالكامل.",
            Phone = "01099990007",
            Email = "dokki.sports@courtbook.eg",
            Latitude = 30.0380,
            Longitude = 31.2050,
            IsActive = true,
            IsVerified = true,
            ApprovalStatus = VenueApprovalStatus.Approved,
            ApprovedById = adminId,
            ApprovedAt = DateTime.UtcNow.AddDays(-12),
            AverageRating = 4.8,
            TotalReviews = 19
        };
        list.Add(new ComplexDefinition
        {
            Venue = v14,
            Courts = new List<Court>
            {
                new Court { Id = Guid.NewGuid(), Name = "ملعب الدقي الخماسي", SportType = SportType.Football, PricePerHour = 220m, SurfaceType = "Artificial Turf", Capacity = 10, IsIndoor = false, IsActive = true, Description = "ملعب خماسي موقع ممتاز وحيوي وسط الدقي." },
                new Court { Id = Guid.NewGuid(), Name = "Dokki Badminton Arena", SportType = SportType.Badminton, PricePerHour = 150m, SurfaceType = "Air Conditioned Wood", Capacity = 4, IsIndoor = true, IsActive = true, Description = "صالة تنس ريشة مكيفة وهادئة ومجهزة للمباريات الفردية والزوجية." }
            }
        });

        // 15. نادي شيراتون سبورت هاب (Sheraton / Heliopolis) [Approved]
        var v15 = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = saraId,
            Name = "نادي شيراتون سبورت هاب",
            City = "Cairo",
            Area = "Sheraton",
            Address = "El Moshir Ahmed Ismail St, Sheraton Heliopolis, Cairo",
            Description = "مجمع رياضي فخم بمصر الجديدة يضم ملاعب بادل بانورامية، صالة كرة طائرة وملاعب كرة سلة خارجية.",
            Phone = "01099990008",
            Email = "sheraton.hub@courtbook.eg",
            Latitude = 30.1050,
            Longitude = 31.3780,
            IsActive = true,
            IsVerified = true,
            ApprovalStatus = VenueApprovalStatus.Approved,
            ApprovedById = adminId,
            ApprovedAt = DateTime.UtcNow.AddDays(-10),
            AverageRating = 4.8,
            TotalReviews = 26
        };
        list.Add(new ComplexDefinition
        {
            Venue = v15,
            Courts = new List<Court>
            {
                new Court { Id = Guid.NewGuid(), Name = "Sheraton Padel Court", SportType = SportType.Padel, PricePerHour = 360m, SurfaceType = "Panoramic Glass", Capacity = 4, IsIndoor = false, IsActive = true, Description = "ملعب بادل حديث بأجواء راقية." },
                new Court { Id = Guid.NewGuid(), Name = "Sheraton Basketball", SportType = SportType.Basketball, PricePerHour = 200m, SurfaceType = "Outdoor Acrylic", Capacity = 10, IsIndoor = false, IsActive = true, Description = "ملعب كرة سلة خارجي واسع." },
                new Court { Id = Guid.NewGuid(), Name = "Sheraton Volleyball", SportType = SportType.Volleyball, PricePerHour = 180m, SurfaceType = "Indoor Hall", Capacity = 12, IsIndoor = true, IsActive = true, Description = "ملعب كرة طائرة مغطى مع شباك حديثة." }
            }
        });

        // 16. أكاديمية النيل بالمنصورة (Mansoura) [Pending Review]
        var v16 = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = mohamedId,
            Name = "أكاديمية النيل بالمنصورة",
            City = "Mansoura",
            Area = "Al Mashaya",
            Address = "El Mashaya El Sofleya, Mansoura",
            Description = "منشأة رياضية جديدة قيد الانتهاء والمراجعة بالمنصورة تشمل ملعب نجيل صناعي وملعب تنس أرضي ترابي.",
            Phone = "01099990009",
            Email = "nile.mansoura@courtbook.eg",
            Latitude = 31.0409,
            Longitude = 31.3785,
            IsActive = true,
            IsVerified = false,
            ApprovalStatus = VenueApprovalStatus.Pending,
            ApprovedById = null,
            ApprovedAt = null,
            AverageRating = 0.0,
            TotalReviews = 0
        };
        list.Add(new ComplexDefinition
        {
            Venue = v16,
            Courts = new List<Court>
            {
                new Court { Id = Guid.NewGuid(), Name = "Mansoura Football Turf", SportType = SportType.Football, PricePerHour = 170m, SurfaceType = "Artificial Turf", Capacity = 10, IsIndoor = false, IsActive = true, Description = "ملعب خماسي جديد بنجيل صناعي ممتاز." },
                new Court { Id = Guid.NewGuid(), Name = "Mansoura Clay Tennis", SportType = SportType.Tennis, PricePerHour = 180m, SurfaceType = "Clay Court", Capacity = 4, IsIndoor = false, IsActive = true, Description = "ملعب تنس ترابي قيد التجهيز النهائي." }
            }
        });

        // 17. مجمع القطامية رويال (Katameya / New Cairo) [Pending Review]
        var v17 = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = ahmedId,
            Name = "مجمع القطامية رويال",
            City = "New Cairo",
            Area = "Katameya",
            Address = "Ring Road Exit, Katameya Heights, New Cairo",
            Description = "مشروع مجمع رياضي فاخر جديد يضم 2 ملاعب بادل وصالة كرة سلة قيد مراجعة الاعتماد والتدقيق الإداري.",
            Phone = "01099990010",
            Email = "katameya.royal@courtbook.eg",
            Latitude = 29.9890,
            Longitude = 31.4200,
            IsActive = true,
            IsVerified = false,
            ApprovalStatus = VenueApprovalStatus.Pending,
            ApprovedById = null,
            ApprovedAt = null,
            AverageRating = 0.0,
            TotalReviews = 0
        };
        list.Add(new ComplexDefinition
        {
            Venue = v17,
            Courts = new List<Court>
            {
                new Court { Id = Guid.NewGuid(), Name = "Royal Padel 1", SportType = SportType.Padel, PricePerHour = 380m, SurfaceType = "Panoramic Glass", Capacity = 4, IsIndoor = false, IsActive = true, Description = "ملعب بادل حديث بمواصفات رويال." },
                new Court { Id = Guid.NewGuid(), Name = "Royal Basketball Hall", SportType = SportType.Basketball, PricePerHour = 220m, SurfaceType = "Parquet Flooring", Capacity = 10, IsIndoor = true, IsActive = true, Description = "صالة سلة مغطاة." }
            }
        });

        // 18. مجمع المهندسين كلوب (Mohandessin / Giza) [Rejected]
        var v18 = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerId = mohamedId,
            Name = "مجمع المهندسين كلوب",
            City = "Giza",
            Area = "Mohandessin",
            Address = "Shehab St, Mohandessin, Giza",
            Description = "ملعب كرة قدم خماسي بالمهندسين تم رفض إدراجه لعدم استيفاء اشتراطات أمان أرضية النجيل الصناعي ومطابقة الصور.",
            Phone = "01099990011",
            Email = "mohandessin.club@courtbook.eg",
            Latitude = 30.0520,
            Longitude = 31.1990,
            IsActive = false,
            IsVerified = false,
            ApprovalStatus = VenueApprovalStatus.Rejected,
            RejectionReason = "Photos provided do not match the facility location and pitch turf requires FIFA safety maintenance certification before listing.",
            ApprovedById = null,
            ApprovedAt = null,
            AverageRating = 0.0,
            TotalReviews = 0
        };
        list.Add(new ComplexDefinition
        {
            Venue = v18,
            Courts = new List<Court>
            {
                new Court { Id = Guid.NewGuid(), Name = "Old Football Pitch", SportType = SportType.Football, PricePerHour = 150m, SurfaceType = "Worn Turf", Capacity = 10, IsIndoor = false, IsActive = false, Description = "الملعب بحاجة لتغيير طبقة النجيل الصناعي." }
            }
        });

        return list;
    }

    public static async Task EnsureCommunityGamesAsync(AppDbContext db, ILogger logger)
    {
        if (await db.Games.CountAsync() >= 6) return;

        logger.LogInformation("Seeding initial community matches with age classifications...");

        var omar = await db.Users.FirstOrDefaultAsync(u => u.Email == "omar@gmail.com");
        var nada = await db.Users.FirstOrDefaultAsync(u => u.Email == "nada@gmail.com");
        var karim = await db.Users.FirstOrDefaultAsync(u => u.Email == "karim@gmail.com");

        if (omar == null || nada == null || karim == null) return;

        var footballCourt = await db.Courts.Include(c => c.Venue).FirstOrDefaultAsync(c => c.SportType == SportType.Football && c.IsActive && c.Venue.ApprovalStatus == VenueApprovalStatus.Approved);
        var padelCourt = await db.Courts.Include(c => c.Venue).FirstOrDefaultAsync(c => c.SportType == SportType.Padel && c.IsActive && c.Venue.ApprovalStatus == VenueApprovalStatus.Approved);
        var tennisCourt = await db.Courts.Include(c => c.Venue).FirstOrDefaultAsync(c => c.SportType == SportType.Tennis && c.IsActive && c.Venue.ApprovalStatus == VenueApprovalStatus.Approved);
        var basketballCourt = await db.Courts.Include(c => c.Venue).FirstOrDefaultAsync(c => c.SportType == SportType.Basketball && c.IsActive && c.Venue.ApprovalStatus == VenueApprovalStatus.Approved);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var games = new List<Game>();

        if (footballCourt != null)
        {
            var g1 = new Game
            {
                Id = Guid.NewGuid(),
                Title = "Friday Night 7v7 Adults Clash",
                SportType = SportType.Football,
                VenueId = footballCourt.VenueId,
                CourtId = footballCourt.Id,
                CreatorId = omar.Id,
                Date = today.AddDays(2),
                StartTime = new TimeOnly(19, 0),
                EndTime = new TimeOnly(20, 30),
                SkillLevel = SkillLevel.Intermediate,
                AgeGroup = AgeGroup.Adults,
                MinAge = 18,
                MaxAge = null,
                MaxPlayers = 14,
                MinPlayers = 10,
                PricePerPlayer = 85m,
                Status = GameStatus.Open,
                Description = "Competitive adult football match. Please arrive 15 minutes early with proper turf shoes.",
                CreatedAt = DateTime.UtcNow
            };
            g1.Participants.Add(new GameParticipant { Id = Guid.NewGuid(), GameId = g1.Id, UserId = omar.Id, IsConfirmed = true, JoinedAt = DateTime.UtcNow });
            games.Add(g1);

            var g2 = new Game
            {
                Id = Guid.NewGuid(),
                Title = "Youth Academy Football Scrimmage",
                SportType = SportType.Football,
                VenueId = footballCourt.VenueId,
                CourtId = footballCourt.Id,
                CreatorId = karim.Id,
                Date = today.AddDays(3),
                StartTime = new TimeOnly(16, 30),
                EndTime = new TimeOnly(18, 0),
                SkillLevel = SkillLevel.AllLevels,
                AgeGroup = AgeGroup.Teens,
                MinAge = 16,
                MaxAge = 17,
                MaxPlayers = 10,
                MinPlayers = 6,
                PricePerPlayer = 50m,
                Status = GameStatus.Open,
                Description = "High school and teen football scrimmage focusing on quick passing and teamwork.",
                CreatedAt = DateTime.UtcNow
            };
            g2.Participants.Add(new GameParticipant { Id = Guid.NewGuid(), GameId = g2.Id, UserId = karim.Id, IsConfirmed = true, JoinedAt = DateTime.UtcNow });
            games.Add(g2);
        }

        if (padelCourt != null)
        {
            var g3 = new Game
            {
                Id = Guid.NewGuid(),
                Title = "Open Padel Doubles League",
                SportType = SportType.Padel,
                VenueId = padelCourt.VenueId,
                CourtId = padelCourt.Id,
                CreatorId = nada.Id,
                Date = today.AddDays(1),
                StartTime = new TimeOnly(10, 0),
                EndTime = new TimeOnly(11, 30),
                SkillLevel = SkillLevel.AllLevels,
                AgeGroup = AgeGroup.AllAges,
                MaxPlayers = 4,
                MinPlayers = 4,
                PricePerPlayer = 120m,
                Status = GameStatus.Open,
                Description = "Morning padel game open to all ages and experience levels. Balls provided.",
                CreatedAt = DateTime.UtcNow
            };
            g3.Participants.Add(new GameParticipant { Id = Guid.NewGuid(), GameId = g3.Id, UserId = nada.Id, IsConfirmed = true, JoinedAt = DateTime.UtcNow });
            games.Add(g3);

            var g4 = new Game
            {
                Id = Guid.NewGuid(),
                Title = "Junior Padel Clinic & Mini Match",
                SportType = SportType.Padel,
                VenueId = padelCourt.VenueId,
                CourtId = padelCourt.Id,
                CreatorId = omar.Id,
                Date = today.AddDays(4),
                StartTime = new TimeOnly(11, 0),
                EndTime = new TimeOnly(12, 30),
                SkillLevel = SkillLevel.Beginner,
                AgeGroup = AgeGroup.Kids,
                MinAge = 6,
                MaxAge = 12,
                MaxPlayers = 4,
                MinPlayers = 2,
                PricePerPlayer = 70m,
                Status = GameStatus.Open,
                Description = "Fun introductory padel game for kids (6-12 years) with supervision and fun mini-sets.",
                CreatedAt = DateTime.UtcNow
            };
            g4.Participants.Add(new GameParticipant { Id = Guid.NewGuid(), GameId = g4.Id, UserId = omar.Id, IsConfirmed = true, JoinedAt = DateTime.UtcNow });
            games.Add(g4);
        }

        if (tennisCourt != null)
        {
            var g5 = new Game
            {
                Id = Guid.NewGuid(),
                Title = "Sunset Singles Tennis Match",
                SportType = SportType.Tennis,
                VenueId = tennisCourt.VenueId,
                CourtId = tennisCourt.Id,
                CreatorId = omar.Id,
                Date = today.AddDays(2),
                StartTime = new TimeOnly(17, 30),
                EndTime = new TimeOnly(19, 0),
                SkillLevel = SkillLevel.Advanced,
                AgeGroup = AgeGroup.Adults,
                MinAge = 18,
                MaxAge = null,
                MaxPlayers = 2,
                MinPlayers = 2,
                PricePerPlayer = 150m,
                Status = GameStatus.Open,
                Description = "Fast-paced advanced singles duel under the evening floodlights.",
                CreatedAt = DateTime.UtcNow
            };
            g5.Participants.Add(new GameParticipant { Id = Guid.NewGuid(), GameId = g5.Id, UserId = omar.Id, IsConfirmed = true, JoinedAt = DateTime.UtcNow });
            games.Add(g5);
        }

        if (basketballCourt != null)
        {
            var g6 = new Game
            {
                Id = Guid.NewGuid(),
                Title = "Weekend 3x3 Half-Court Battle",
                SportType = SportType.Basketball,
                VenueId = basketballCourt.VenueId,
                CourtId = basketballCourt.Id,
                CreatorId = nada.Id,
                Date = today.AddDays(3),
                StartTime = new TimeOnly(18, 0),
                EndTime = new TimeOnly(19, 30),
                SkillLevel = SkillLevel.AllLevels,
                AgeGroup = AgeGroup.AllAges,
                MaxPlayers = 6,
                MinPlayers = 4,
                PricePerPlayer = 60m,
                Status = GameStatus.Open,
                Description = "Casual half-court 3v3 basketball runs with music and refreshments.",
                CreatedAt = DateTime.UtcNow
            };
            g6.Participants.Add(new GameParticipant { Id = Guid.NewGuid(), GameId = g6.Id, UserId = nada.Id, IsConfirmed = true, JoinedAt = DateTime.UtcNow });
            games.Add(g6);
        }

        if (games.Any())
        {
            await db.Games.AddRangeAsync(games);
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded {Count} community games across diverse sports & age groups.", games.Count);
        }
    }

    public static async Task EnsureHistoricalOwnerBalancesBackfilledAsync(AppDbContext db, ILogger logger)
    {
        var owners = await db.Users
            .Where(u => u.Role == Role.Owner)
            .ToListAsync();

        var cutoff = DateTime.UtcNow.AddHours(-24);

        foreach (var owner in owners)
        {
            var existingBalance = await db.OwnerBalances.FirstOrDefaultAsync(b => b.OwnerId == owner.Id);
            if (existingBalance == null)
            {
                // Query all captured bookings for venues belonging to this owner
                var capturedBookings = await db.Bookings
                    .Include(b => b.Payment)
                    .Include(b => b.Court).ThenInclude(c => c.Venue)
                    .Where(b => b.Court.Venue.OwnerId == owner.Id
                             && b.Payment != null
                             && (b.Payment.Status == PaymentStatus.Completed || b.Payment.Status == PaymentStatus.PartiallyRefunded)
                             && b.PaymentStatus != PaymentStatus.Refunded
                             && !(b.Payment.Method == PaymentMethod.PayAtFacility && b.PaymentStatus == PaymentStatus.Pending)
                             && b.Payment.Status != PaymentStatus.Processing)
                    .ToListAsync();

                decimal availableNet = 0m;
                decimal pendingNet = 0m;

                foreach (var b in capturedBookings)
                {
                    decimal net;
                    if (b.Payment!.Status == PaymentStatus.PartiallyRefunded)
                    {
                        var gross = b.CancellationFee;
                        var comm = Math.Round(gross * 0.05m, 2);
                        net = gross - comm;
                    }
                    else
                    {
                        net = b.Payment.OwnerNetAmount;
                    }

                    if (net <= 0m) continue;

                    if (b.EndTime.AddHours(24) <= DateTime.UtcNow)
                    {
                        availableNet += net;
                    }
                    else
                    {
                        pendingNet += net;
                    }
                }

                db.OwnerBalances.Add(new OwnerBalance
                {
                    OwnerId = owner.Id,
                    PendingBalance = pendingNet,
                    AvailableBalance = availableNet,
                    InFlightBalance = 0m,
                    TotalPaidOut = 0m,
                    TotalRefunded = 0m,
                    OutstandingDeficit = 0m,
                    Currency = "EGP",
                    ConcurrencyStamp = Guid.NewGuid(),
                    UpdatedAt = DateTime.UtcNow
                });

                logger.LogInformation("Backfilled OwnerBalance for owner {OwnerId}: Available EGP {Available}, Pending EGP {Pending}.",
                    owner.Id, availableNet, pendingNet);
            }
        }

        await db.SaveChangesAsync();
    }
}

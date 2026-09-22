using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Infrastructure.Persistence;

/// <summary>
/// Main EF Core database context for PlaySpot.
/// All Fluent API configurations are auto-discovered from this assembly.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<PlayerPreference> PlayerPreferences => Set<PlayerPreference>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<Court> Courts => Set<Court>();
    public DbSet<CourtSchedule> CourtSchedules => Set<CourtSchedule>();
    public DbSet<VenueImage> VenueImages => Set<VenueImage>();
    public DbSet<CourtImage> CourtImages => Set<CourtImage>();
    public DbSet<Amenity> Amenities => Set<Amenity>();
    public DbSet<VenueAmenity> VenueAmenities => Set<VenueAmenity>();
    public DbSet<OperatingHour> OperatingHours => Set<OperatingHour>();
    public DbSet<PriceRule> PriceRules => Set<PriceRule>();
    public DbSet<CancellationPolicy> CancellationPolicies => Set<CancellationPolicy>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<GameParticipant> GameParticipants => Set<GameParticipant>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<TermsDocument> TermsDocuments => Set<TermsDocument>();
    public DbSet<TermsAcceptance> TermsAcceptances => Set<TermsAcceptance>();

    // Phase 7: Payment infrastructure
    public DbSet<IdempotencyLog> IdempotencyLogs => Set<IdempotencyLog>();
    public DbSet<TransactionLedger> TransactionLedger => Set<TransactionLedger>();

    // Phase 9.1: Mobile API & Refresh Token Infrastructure
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // Phase 9.2: Advanced Matchmaking & Lobby Infrastructure
    public DbSet<PlayerSportSkill> PlayerSportSkills => Set<PlayerSportSkill>();

    // Phase 9.3: Community & Marketplace Infrastructure
    public DbSet<GameInvitation> GameInvitations => Set<GameInvitation>();
    public DbSet<PlayerConnection> PlayerConnections => Set<PlayerConnection>();

    // Phase 9.4: Financial Payouts & Settlement Infrastructure
    public DbSet<OwnerBalance> OwnerBalances => Set<OwnerBalance>();
    public DbSet<OwnerPayoutMethod> OwnerPayoutMethods => Set<OwnerPayoutMethod>();
    public DbSet<PayoutRequest> PayoutRequests => Set<PayoutRequest>();
    public DbSet<SettlementBatch> SettlementBatches => Set<SettlementBatch>();
    public DbSet<SettlementItem> SettlementItems => Set<SettlementItem>();
    public DbSet<RecoveryObligation> RecoveryObligations => Set<RecoveryObligation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Automatically applies all IEntityTypeConfiguration<T> classes in this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}

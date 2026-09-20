using CourtBook.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Infrastructure.Persistence;

/// <summary>
/// Main EF Core database context for CourtBook.
/// All Fluent API configurations are auto-discovered from this assembly.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<Court> Courts => Set<Court>();
    public DbSet<CourtSchedule> CourtSchedules => Set<CourtSchedule>();
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Automatically applies all IEntityTypeConfiguration<T> classes in this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}

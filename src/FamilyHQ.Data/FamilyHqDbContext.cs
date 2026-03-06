using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Data;

public class FamilyHqDbContext : DbContext
{
    public FamilyHqDbContext(DbContextOptions<FamilyHqDbContext> options)
        : base(options)
    {
    }

    public DbSet<CalendarInfo> Calendars => Set<CalendarInfo>();
    public DbSet<CalendarEvent> Events => Set<CalendarEvent>();
    public DbSet<SyncState> SyncStates => Set<SyncState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FamilyHqDbContext).Assembly);
    }
}

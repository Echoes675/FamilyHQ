using FamilyHQ.Core.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Data;

public class FamilyHqDbContext : DbContext, IDataProtectionKeyContext
{
    public FamilyHqDbContext(DbContextOptions<FamilyHqDbContext> options)
        : base(options)
    {
    }

    public DbSet<CalendarInfo> Calendars => Set<CalendarInfo>();
    public DbSet<CalendarEvent> Events => Set<CalendarEvent>();
    public DbSet<SyncState> SyncStates => Set<SyncState>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();
    public DbSet<CircadianBoundaries> CircadianBoundaries => Set<CircadianBoundaries>();
    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();

    /// <summary>
    /// Required for ASP.NET Core Data Protection key storage in the database.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FamilyHqDbContext).Assembly);
    }

    public override int SaveChanges()
    {
        ConvertDateTimeOffsetsToUtc();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ConvertDateTimeOffsetsToUtc();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ConvertDateTimeOffsetsToUtc();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ConvertDateTimeOffsetsToUtc();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ConvertDateTimeOffsetsToUtc()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            foreach (var property in entry.Properties)
            {
                if (property.Metadata.ClrType == typeof(DateTimeOffset))
                {
                    if (property.CurrentValue is DateTimeOffset value && value.Offset != TimeSpan.Zero)
                    {
                        property.CurrentValue = value.ToUniversalTime();
                    }
                }
                else if (property.Metadata.ClrType == typeof(DateTimeOffset?))
                {
                    if (property.CurrentValue is DateTimeOffset value && value.Offset != TimeSpan.Zero)
                    {
                        property.CurrentValue = value.ToUniversalTime();
                    }
                }
            }
        }
    }
}

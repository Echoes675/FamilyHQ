using FamilyHQ.Simulator.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FamilyHQ.Simulator.Data;

public class SimContext : DbContext
{
    public SimContext(DbContextOptions<SimContext> options) : base(options) { }
    public DbSet<SimulatedEvent> Events => Set<SimulatedEvent>();
    public DbSet<SimulatedCalendar> Calendars => Set<SimulatedCalendar>();
    public DbSet<SimulatedUser> Users => Set<SimulatedUser>();
    public DbSet<SimulatedEventAttendee> EventAttendees => Set<SimulatedEventAttendee>();
    public DbSet<SimulatedWeather> SimulatedWeather => Set<SimulatedWeather>();
    public DbSet<SimulatedLocation> SimulatedLocations => Set<SimulatedLocation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SimulatedEvent>(entity =>
        {
            entity.ToTable("SimulatedEvents");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(255);
            entity.Property(e => e.CalendarId).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Summary).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Location).HasMaxLength(1000);
            entity.Property(e => e.UserId).HasMaxLength(255);
        });

        modelBuilder.Entity<SimulatedCalendar>(entity =>
        {
            entity.ToTable("SimulatedCalendars");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(255);
            entity.Property(e => e.Summary).IsRequired().HasMaxLength(500);
            entity.Property(e => e.BackgroundColor).HasMaxLength(50);
            entity.Property(e => e.UserId).HasMaxLength(255);
        });

        modelBuilder.Entity<SimulatedUser>(entity =>
        {
            entity.ToTable("SimulatedUsers");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(255);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(255);
        });

        modelBuilder.Entity<SimulatedEventAttendee>(entity =>
        {
            entity.ToTable("SimulatedEventAttendees");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EventId).IsRequired().HasMaxLength(255);
            entity.Property(e => e.AttendeeCalendarId).IsRequired().HasMaxLength(255);
            entity.HasIndex(e => new { e.EventId, e.AttendeeCalendarId }).IsUnique();
        });

        modelBuilder.Entity<SimulatedLocation>(entity =>
        {
            entity.ToTable("SimulatedLocations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PlaceName).IsRequired().HasMaxLength(500);
        });

        // Universal UTC conversion for all DateTime properties
        var dateTimeConverter = new ValueConverter<DateTime, DateTime>(
            v => v.ToUniversalTime(),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        var nullableDateTimeConverter = new ValueConverter<DateTime?, DateTime?>(
            v => v.HasValue ? v.Value.ToUniversalTime() : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(dateTimeConverter);
                }
                else if (property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(nullableDateTimeConverter);
                }
            }
        }
    }
}

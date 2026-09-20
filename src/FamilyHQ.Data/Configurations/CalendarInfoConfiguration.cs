using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyHQ.Data.Configurations;

public class CalendarInfoConfiguration : IEntityTypeConfiguration<CalendarInfo>
{
    public void Configure(EntityTypeBuilder<CalendarInfo> builder)
    {
        builder.ToTable("Calendars");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.GoogleCalendarId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(c => c.DisplayName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(c => c.Color)
            .HasMaxLength(50);

        builder.Property(c => c.UserId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(c => c.IsShared)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(c => c.WebhooksSupported)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(c => c.DisplayOrder)
            .IsRequired()
            .HasDefaultValue(0);

        // FHQ-164: the calendar's default zone from Google's calendar resource. Same shape and cap
        // as DayTheme.IanaTimeZone / DisplaySetting.IanaTimeZone.
        builder.Property(c => c.IanaTimeZone)
            .HasMaxLength(64);

        // FHQ-189: DefaultReminders is read off Google on every calendar-list sync but not yet
        // persisted — that is a later task. Without this, EF's default convention treats the
        // reference-typed property as an unconfigured navigation to a keyless entity and throws
        // building the model (mirrors CalendarEventConfiguration.Ignore(e => e.Reminders)).
        builder.Ignore(c => c.DefaultReminders);

        builder.HasIndex(c => new { c.GoogleCalendarId, c.UserId }).IsUnique();

        builder.HasOne(c => c.SyncState)
            .WithOne(s => s.CalendarInfo)
            .HasForeignKey<SyncState>(s => s.CalendarInfoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

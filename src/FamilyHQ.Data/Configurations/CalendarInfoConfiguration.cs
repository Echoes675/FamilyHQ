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

        // FHQ-189: the calendar's default reminders (see CalendarInfo.DefaultReminders).
        // FHQ-205: stored through a value converter rather than OwnsOne(...).ToJson() — see the
        // identical configuration (and its rationale) on CalendarEventConfiguration.Reminders.
        // This is the property whose owned-collection shadow key took calendar syncing down:
        // RefreshCalendarDefaultsAsync assigns it on a detached calendar that
        // CalendarRepository.UpdateCalendarAsync then saves through Calendars.Update(...).
        builder.Property(c => c.DefaultReminders)
            .HasConversion(EventRemindersConversion.Converter, EventRemindersConversion.Comparer)
            .HasColumnType(EventRemindersConversion.ColumnType);

        builder.HasIndex(c => new { c.GoogleCalendarId, c.UserId }).IsUnique();

        builder.HasOne(c => c.SyncState)
            .WithOne(s => s.CalendarInfo)
            .HasForeignKey<SyncState>(s => s.CalendarInfoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

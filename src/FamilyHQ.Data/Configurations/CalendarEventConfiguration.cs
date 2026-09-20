using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyHQ.Data.Configurations;

public class CalendarEventConfiguration : IEntityTypeConfiguration<CalendarEvent>
{
    public void Configure(EntityTypeBuilder<CalendarEvent> builder)
    {
        builder.ToTable("Events");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.GoogleEventId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.Title)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.Location)
            .HasMaxLength(1000);

        builder.Property(e => e.Start)
            .HasConversion(v => v.ToUniversalTime(), v => v);

        builder.Property(e => e.End)
            .HasConversion(v => v.ToUniversalTime(), v => v);

        builder.Property(e => e.OriginalStartTime)
            .HasConversion(
                v => v.HasValue ? v.Value.ToUniversalTime() : v,
                v => v);

        // Google series ID — same shape as GoogleEventId, so the same 255 cap.
        builder.Property(e => e.GoogleRecurringEventId)
            .HasMaxLength(255);

        // RRULE text (e.g. "RRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR"). Bounded in practice;
        // capped for consistency with the entity's other string columns.
        builder.Property(e => e.RecurrenceRule)
            .HasMaxLength(1000);

        // FHQ-170/FHQ-164: the IANA id Google reports as start.timeZone. A zone id is a plain
        // varchar with no timestamp semantics — nothing for Postgres to convert — so it does not
        // re-open the DateTimeOffset/UTC problem. 64 matches DayTheme.IanaTimeZone and
        // DisplaySetting.IanaTimeZone; the longest id in the tz database is comfortably inside it.
        builder.Property(e => e.IanaTimeZone)
            .HasMaxLength(64);

        // FHQ-189: Google replaces `reminders` as a whole object and FamilyHQ only ever reads and
        // writes it together with its event, so it is stored as one JSON document rather than a
        // child table. A child table would add a join to the reminders-timeline query that runs on
        // every kiosk load, plus a delete-and-reinsert on every sync — the race FHQ-111 was about.
        // OwnsOne(...).ToJson() keeps FamilyHQ.Data provider-agnostic; Npgsql maps it to jsonb.
        builder.OwnsOne(e => e.Reminders, r =>
        {
            r.ToJson();
            r.OwnsMany(x => x.Overrides);
        });

        builder.HasIndex(e => e.GoogleEventId).IsUnique();
        builder.HasIndex(e => e.Start);
        builder.HasIndex(e => e.End);
        builder.HasIndex(e => e.GoogleRecurringEventId);
        builder.HasIndex(e => new { e.GoogleRecurringEventId, e.OriginalStartTime });

        builder.Property(e => e.OwnerCalendarInfoId).IsRequired();

        builder.HasOne<CalendarInfo>()
            .WithMany()
            .HasForeignKey(e => e.OwnerCalendarInfoId)
            .OnDelete(DeleteBehavior.Restrict);

        // ContentHash is a transient property populated from Google extendedProperties.
        // It is never persisted — the DB is the authoritative source for event data,
        // and the hash is only used in-flight to detect webhook self-echoes (FHQ-30).
        builder.Ignore(e => e.ContentHash);

        // EventMembers junction: which family members are assigned to this event.
        builder.HasMany(e => e.Members)
            .WithMany()
            .UsingEntity(j => j.ToTable("EventMembers"));
    }
}

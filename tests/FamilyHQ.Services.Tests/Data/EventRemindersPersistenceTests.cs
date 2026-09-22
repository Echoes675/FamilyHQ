using System;
using System.Collections.Generic;
using System.Linq;
using FamilyHQ.Core.Models;
using FamilyHQ.Data;
using FamilyHQ.Data.Configurations;
using FamilyHQ.Data.PostgreSQL.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace FamilyHQ.Services.Tests.Data;

/// <summary>
/// Regression coverage for the three defects that <c>OwnsOne(...).ToJson()</c> +
/// <c>OwnsMany(...)</c> produced on <see cref="EventReminders"/> — two found by the FHQ-189
/// whole-branch review, the third being the FHQ-205 production incident. All three are caught by
/// building the REAL EF model rather than mocking <c>ICalendarRepository</c>, which is what every
/// other test in this project does and exactly why none of them was caught earlier.
/// </summary>
/// <remarks>
/// Modelled on the existing <see cref="FamilyHQ.Services.Tests.Repositories.NpgsqlModelCustomizerTests"/>:
/// a real <see cref="FamilyHqDbContext"/> is built against the real Npgsql customizer with a
/// connection string that is never opened — model construction and change tracking (<c>Add</c>,
/// <c>Update</c>, <c>ChangeTracker.DetectChanges</c>) touch no database. No InMemory provider, no
/// SQLite, no new test package.
/// <para>
/// The FHQ-205 tests go one step further and call <c>SaveChanges</c>. That is still database-free:
/// EF's <c>PrepareToSave</c> — where the shadow-key failure was raised — runs before any connection
/// is opened, so the failure the incident produced is reachable here and a connection error is the
/// PASS signal.
/// </para>
/// </remarks>
public class EventRemindersPersistenceTests
{
    /// <summary>
    /// The shadow key EF synthesised for each element of the owned <c>Overrides</c> collection.
    /// Its absence from a save failure is what these tests assert.
    /// </summary>
    private const string SynthesizedOrdinal = "__synthesizedOrdinal";

    private static FamilyHqDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FamilyHqDbContext>()
            .UseNpgsql("Host=localhost;Database=irrelevant")
            .ReplaceService<IModelCustomizer, NpgsqlModelCustomizer>()
            .Options;
        return new FamilyHqDbContext(options);
    }

    private static EventReminders Reminders(params int[] minutes) =>
        EventReminders.Explicit(minutes.Select(m => new EventReminder("popup", m)).ToList());

    private static CalendarInfo DetachedCalendar(EventReminders? reminders) => new()
    {
        Id = Guid.NewGuid(),
        GoogleCalendarId = "calendar-fhq-205",
        UserId = "user-1",
        DisplayName = "Detached",
        DefaultReminders = reminders
    };

    private static CalendarEvent DetachedEvent(EventReminders? reminders) => new()
    {
        Id = Guid.NewGuid(),
        GoogleEventId = "event-fhq-205",
        Title = "Detached",
        OwnerCalendarInfoId = Guid.NewGuid(),
        Reminders = reminders
    };

    /// <summary>
    /// Asserts the save never fails the way the FHQ-205 incident failed. An exception about
    /// reaching the database is expected and means the save got past <c>PrepareToSave</c>.
    /// </summary>
    private static void SaveChangesMustNotFailOnTheShadowKey(FamilyHqDbContext context, string because)
    {
        var thrown = Record.Exception(() => context.SaveChanges());

        var failedOnShadowKey = thrown is InvalidOperationException
            && thrown.Message.Contains(SynthesizedOrdinal, StringComparison.Ordinal);

        failedOnShadowKey.Should().BeFalse(because);
    }

    // ── C1 guard (FHQ-189) ────────────────────────────────────────────────────────────────

    [Fact]
    public void Overrides_DefaultCollection_IsNotFixedSize()
    {
        // `Overrides { get; init; } = []` on an IReadOnlyList<T> target compiles to
        // Array.Empty<EventReminder>() — fixed-size. Under the owned-entity mapping EF's navigation
        // fixup called Add(...) on this EXACT instance while materialising a TRACKED query, and a
        // fixed-size collection threw NotSupportedException the moment it did ("Collection was of a
        // fixed size."), taking down every tracked read of an event that has reminders.
        //
        // FHQ-205 removed the fixup (there is no owned navigation any more), but the requirement
        // stands: the value converter deserialises INTO a mutable List, and the comparer's snapshot
        // is taken from it, so a fixed-size default would still be the wrong shape to hand EF.
        var overrides = new EventReminders().Overrides;

        ((ICollection<EventReminder>)overrides).IsReadOnly.Should().BeFalse(
            "the converter and the comparer both treat Overrides as a mutable list; a fixed-size " +
            "Array.Empty<T>() is not a safe default for a collection EF may hand back and forth");
    }

    // ── C2 guard (FHQ-189, restated for FHQ-205) ──────────────────────────────────────────

    [Fact]
    public void TwoEventsInheritingTheCalendarDefault_EachGetTheirOwnRemindersInstance()
    {
        // Originally the whole-branch review's C2 guard: MapReminders returned the shared static
        // EventReminders.InheritsCalendarDefault for every useDefault:true event, and an EF OWNED
        // type cannot be shared across two owners — the second Add threw, or worse, EF silently
        // re-parented the single instance and wrote the first row's Reminders back as NULL.
        //
        // FHQ-205 replaced the owned mapping with a value converter, so sharing one CLR instance
        // between two owners is no longer a persistence fault: the converter serialises the value
        // independently for each row and EF tracks no EventReminders entity at all. The guard is
        // KEPT, and its assertion restated, because the reason InheritsCalendarDefault is a factory
        // PROPERTY rather than a static readonly field has not changed: EventReminders is mutated
        // in place by callers that build up overrides, and a process-wide singleton would let one
        // event's edit alter every other event that inherits the default.
        using var context = CreateContext();

        var calendarId = Guid.NewGuid();

        var event1 = new CalendarEvent
        {
            Id = Guid.NewGuid(),
            GoogleEventId = "event-reminders-persistence-1",
            Title = "One",
            OwnerCalendarInfoId = calendarId,
            Reminders = EventReminders.InheritsCalendarDefault
        };
        var event2 = new CalendarEvent
        {
            Id = Guid.NewGuid(),
            GoogleEventId = "event-reminders-persistence-2",
            Title = "Two",
            OwnerCalendarInfoId = calendarId,
            Reminders = EventReminders.InheritsCalendarDefault
        };

        context.Add(event1);
        context.Add(event2);
        context.ChangeTracker.DetectChanges();

        event1.Reminders.Should().NotBeNull();
        event2.Reminders.Should().NotBeNull();
        ReferenceEquals(event1.Reminders, event2.Reminders).Should().BeFalse(
            "InheritsCalendarDefault must stay a factory property yielding a fresh instance — a " +
            "shared singleton would let one event's reminder edit alter every other event");

        context.ChangeTracker.Entries<EventReminders>().Should().BeEmpty(
            "FHQ-205: EventReminders is a value mapped through a converter, not an owned entity, " +
            "so EF must track no EventReminders entry for either event");
    }

    // ── C3 guards: the FHQ-205 incident ───────────────────────────────────────────────────

    [Fact]
    public void DetachedCalendarWithReminders_CanBeSaved_ThroughRepositoryStyleUpdate()
    {
        // The exact production shape. CalendarRepository.GetCalendarsAsync returns calendars
        // AsNoTracking, CalendarSyncService.RefreshCalendarDefaultsAsync assigns DefaultReminders
        // from the calendarList response, and CalendarRepository.UpdateCalendarAsync calls
        // Calendars.Update(...) on that DETACHED graph. Under the owned-entity mapping SaveChanges
        // threw on the synthesized ordinal of each Overrides element, and calendar syncing stopped.
        using var context = CreateContext();

        var calendar = DetachedCalendar(Reminders(10, 30, 60));
        context.Calendars.Update(calendar);

        context.Entry(calendar).State.Should().Be(EntityState.Modified,
            "the test must actually be saving the calendar for the assertion below to mean anything");

        SaveChangesMustNotFailOnTheShadowKey(context,
            "a detached calendar carrying reminders is how every production calendar is saved; " +
            "the save must reach the database rather than fail on an owned collection's shadow key");
    }

    [Fact]
    public void DetachedEventWithReminders_CanBeSaved_ThroughRepositoryStyleUpdate()
    {
        // CalendarEvent.Reminders carried the identical mapping, so the identical latent failure.
        // Nothing proved that path was safe before FHQ-205.
        using var context = CreateContext();

        var calendarEvent = DetachedEvent(Reminders(15));
        context.Events.Update(calendarEvent);

        context.Entry(calendarEvent).State.Should().Be(EntityState.Modified);

        SaveChangesMustNotFailOnTheShadowKey(context,
            "an event's reminders must survive a detached Update for exactly the same reason a " +
            "calendar's defaults must");
    }

    [Fact]
    public void DetachedUpdate_TracksNoOwnedEventRemindersEntries()
    {
        // This is the test that pins the FIX rather than the symptom. With a value converter there
        // is no owned entity, therefore no shadow key, therefore nothing that can be "unknown when
        // attempting to save changes". If this assertion ever fails, the owned mapping is back and
        // the incident is reachable again, whatever the other tests say.
        using var context = CreateContext();

        var calendar = DetachedCalendar(Reminders(30));
        var calendarEvent = DetachedEvent(Reminders(30));

        context.Calendars.Update(calendar);
        context.Events.Update(calendarEvent);

        context.ChangeTracker.Entries<EventReminders>().Should().BeEmpty(
            "EventReminders must be mapped as a converted value, not an owned entity");
        context.ChangeTracker.Entries<EventReminder>().Should().BeEmpty(
            "an owned EventReminder element is what carried the __synthesizedOrdinal shadow key");
    }

    [Fact]
    public void DetachedUpdate_MarksRemindersModified_WhenTheValueChanged()
    {
        using var context = CreateContext();

        var calendar = DetachedCalendar(Reminders(15));
        context.Calendars.Update(calendar);

        var property = context.Entry(calendar).Property(c => c.DefaultReminders);

        property.IsModified.Should().BeTrue(
            "the reminders must be written as part of the calendar's UPDATE statement");
        property.CurrentValue.Should().NotBeNull();
    }

    [Fact]
    public void DetachedUpdate_MarksRemindersModified_WhenClearedToNull()
    {
        // The silent half of the incident. Under the owned mapping a detached clear did NOT throw —
        // it saved, and read back unchanged, because EF had no tracked owned entry to delete. As a
        // converted scalar, null is just another value the UPDATE carries.
        using var context = CreateContext();

        var calendar = DetachedCalendar(reminders: null);
        context.Calendars.Update(calendar);

        var property = context.Entry(calendar).Property(c => c.DefaultReminders);

        property.IsModified.Should().BeTrue(
            "clearing the reminders on a detached calendar must clear the column, not be dropped");
        property.CurrentValue.Should().BeNull();
    }

    [Fact]
    public void DetachedUpdate_MarksEventRemindersModified_WhenTheValueChanged()
    {
        // m2: the same assertion for CalendarEvent. The two entities carry the IDENTICAL mapping,
        // and it was CalendarInfo that took production down purely because that path ran first.
        using var context = CreateContext();

        var calendarEvent = new CalendarEvent
        {
            Id = Guid.NewGuid(),
            GoogleEventId = "event-detached-update",
            Title = "Detached",
            OwnerCalendarInfoId = Guid.NewGuid(),
            Reminders = Reminders(15)
        };
        context.Events.Update(calendarEvent);

        var property = context.Entry(calendarEvent).Property(e => e.Reminders);

        property.IsModified.Should().BeTrue(
            "an event's reminders must be written as part of its UPDATE statement");
        property.CurrentValue.Should().NotBeNull();
    }

    // ── The comparer is part of the mapping, not an optional extra ────────────────────

    [Theory]
    [InlineData(typeof(CalendarInfo), nameof(CalendarInfo.DefaultReminders))]
    [InlineData(typeof(CalendarEvent), nameof(CalendarEvent.Reminders))]
    public void RemindersProperty_UsesTheStructuralValueComparer(Type entityType, string propertyName)
    {
        // FHQ-205 review finding M1. Deleting EventRemindersConversion.Comparer from the
        // HasConversion(...) calls leaves the ENTIRE unit suite green, while against a real
        // database that build silently drops an in-place mutation of Overrides (EF compares the
        // two EventReminders instances by reference, sees no change, and writes nothing) AND
        // reports a change on every sync where Google merely reordered the array -- which makes
        // SyncResult.HadChanges true every hourly cycle, firing a placement reconcile and a kiosk
        // EventsUpdated broadcast on every no-op sync, breaking FHQ-44.
        //
        // Without a value comparer EF cannot compare a converted reference type structurally, so
        // the comparer is load-bearing rather than decorative. Nothing else pins it, and "a change
        // no test can see" is precisely the shape that caused this incident.
        using var context = CreateContext();

        var property = context.Model.FindEntityType(entityType)!.FindProperty(propertyName)!;

        property.GetValueComparer().Should().BeSameAs(
            EventRemindersConversion.Comparer,
            "EF needs a structural comparer to detect a change to a converted reference type; " +
            "without it, reminder changes are silently not written and unchanged reminders are " +
            "reported as changed");
        property.GetValueConverter().Should().BeSameAs(
            EventRemindersConversion.Converter,
            "both entities must share one converter so their stored JSON cannot drift apart");
    }

    // ── Converter round-trip ──────────────────────────────────────────────────────────────

    private static EventReminders? RoundTrip(EventReminders? value)
    {
        var json = EventRemindersConversion.Converter.ConvertToProvider(value);
        return (EventReminders?)EventRemindersConversion.Converter.ConvertFromProvider(json);
    }

    [Fact]
    public void Converter_RoundTrips_NotYetSynced_AsNull()
    {
        // The fourth state: the null reference, meaning "not yet synced" — the marker the FHQ-189
        // backfill keys on. It must stay distinguishable from every non-null state.
        RoundTrip(null).Should().BeNull();
    }

    [Fact]
    public void Converter_RoundTrips_InheritsCalendarDefault()
    {
        var result = RoundTrip(EventReminders.InheritsCalendarDefault);

        result.Should().NotBeNull();
        result!.UseDefault.Should().BeTrue();
        result.Overrides.Should().BeEmpty();
    }

    [Fact]
    public void Converter_RoundTrips_ExplicitlyNone()
    {
        // useDefault:false with no reminders. Must not collapse into InheritsCalendarDefault.
        var result = RoundTrip(EventReminders.ExplicitlyNone);

        result.Should().NotBeNull();
        result!.UseDefault.Should().BeFalse();
        result.Overrides.Should().BeEmpty();
    }

    [Fact]
    public void Converter_RoundTrips_ExplicitOverrides_IncludingUnknownMethodAndNegativeMinutes()
    {
        // Method is a STRING and Minutes is SIGNED on purpose (see the EventReminder docs): an
        // unknown method and an out-of-range minutes value must survive storage rather than be
        // dropped or "corrected".
        var original = EventReminders.Explicit(
        [
            new EventReminder("popup", 30),
            new EventReminder("email", 1440),
            new EventReminder("sms", -5)
        ]);

        var result = RoundTrip(original);

        result.Should().NotBeNull();
        result!.UseDefault.Should().BeFalse();
        result.Overrides.Should().Equal(original.Overrides);
    }

    [Fact]
    public void Converter_SerialisesGooglesOwnPropertyNames()
    {
        // The stored shape is load-bearing: production rows written by the previous OwnsOne(...)
        // mapping must keep deserialising, and architecture.md states this column holds Google's
        // own shape rather than a .NET-cased approximation of it.
        var json = (string?)EventRemindersConversion.Converter.ConvertToProvider(Reminders(30));

        json.Should().Be("""{"useDefault":false,"overrides":[{"method":"popup","minutes":30}]}""");
    }

    [Fact]
    public void Converter_Deserialises_MissingOverridesKey_ToAnEmptyList()
    {
        // Google OMITS `overrides` entirely for the explicitly-none shape, so a stored document
        // without the key is normal and must never yield a null Overrides.
        var result = (EventReminders?)EventRemindersConversion.Converter
            .ConvertFromProvider("""{"useDefault":false}""");

        result.Should().NotBeNull();
        result!.Overrides.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Converter_Deserialises_OverridesIntoAMutableList()
    {
        // Same requirement as the C1 guard, at the point the value actually comes back from the
        // database — this is the instance the comparer snapshots and EF hands to callers.
        var result = (EventReminders?)EventRemindersConversion.Converter
            .ConvertFromProvider("""{"useDefault":false,"overrides":[{"method":"popup","minutes":30}]}""");

        ((ICollection<EventReminder>)result!.Overrides).IsReadOnly.Should().BeFalse();
    }

    // ── Comparer correctness ──────────────────────────────────────────────────────────────

    [Fact]
    public void Comparer_TreatsDifferentOverrideOrder_AsEqual()
    {
        // Google reorders the overrides array (FHQ-193, fixture 25). An order-sensitive comparison
        // would report a change on every sync and write the event back to Google for nothing.
        var left = EventReminders.Explicit([new EventReminder("popup", 30), new EventReminder("email", 10)]);
        var right = EventReminders.Explicit([new EventReminder("email", 10), new EventReminder("popup", 30)]);

        EventRemindersConversion.Comparer.Equals(left, right).Should().BeTrue();
        EventRemindersConversion.Comparer.GetHashCode(left).Should().Be(
            EventRemindersConversion.Comparer.GetHashCode(right),
            "a hash that disagrees with the equality causes missed updates");
    }

    [Fact]
    public void Comparer_TreatsDifferentDuplicateCounts_AsUnequal()
    {
        // Order-insensitive, but duplicate-COUNTING: two identical reminders are not one reminder.
        var left = EventReminders.Explicit([new EventReminder("popup", 30), new EventReminder("popup", 30)]);
        var right = EventReminders.Explicit([new EventReminder("popup", 30)]);

        EventRemindersConversion.Comparer.Equals(left, right).Should().BeFalse();
    }

    [Fact]
    public void Comparer_DistinguishesTheThreeNonNullStates()
    {
        EventRemindersConversion.Comparer
            .Equals(EventReminders.InheritsCalendarDefault, EventReminders.ExplicitlyNone)
            .Should().BeFalse("useDefault:true and useDefault:false are different instructions to Google");

        EventRemindersConversion.Comparer
            .Equals(EventReminders.ExplicitlyNone, Reminders(30))
            .Should().BeFalse();

        EventRemindersConversion.Comparer.Equals(null, EventReminders.ExplicitlyNone)
            .Should().BeFalse("not-yet-synced is not the same as explicitly no reminders");

        EventRemindersConversion.Comparer.Equals(null, null).Should().BeTrue();
    }

    [Fact]
    public void Comparer_Snapshot_DeepCopiesTheOverrides()
    {
        // Without a deep copy the snapshot aliases the live list, and EF cannot see a mutation of
        // Overrides at all — the change would simply never be written.
        var original = EventReminders.Explicit([new EventReminder("popup", 30)]);

        var snapshot = EventRemindersConversion.Comparer.Snapshot(original);

        snapshot.Should().NotBeNull();
        ReferenceEquals(snapshot!.Overrides, original.Overrides).Should().BeFalse();

        ((ICollection<EventReminder>)original.Overrides).Add(new EventReminder("email", 10));

        snapshot.Overrides.Should().ContainSingle("the snapshot must not move with the live value");
        EventRemindersConversion.Comparer.Equals(snapshot, original).Should().BeFalse(
            "EF detects the change by comparing the value against its snapshot");
    }
}

using System.Collections.Generic;
using FamilyHQ.Core.Models;
using FamilyHQ.Data;
using FamilyHQ.Data.PostgreSQL.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace FamilyHQ.Services.Tests.Data;

/// <summary>
/// FHQ-189 final-review fix wave. Regression coverage for two Critical defects the whole-branch
/// review found by building the REAL EF model — not by mocking <c>ICalendarRepository</c>, which is
/// what every other test in this project does and exactly why neither defect was caught earlier.
/// </summary>
/// <remarks>
/// Modelled on the existing <see cref="FamilyHQ.Services.Tests.Repositories.NpgsqlModelCustomizerTests"/>:
/// a real <see cref="FamilyHqDbContext"/> is built against the real Npgsql customizer with a
/// connection string that is never opened — model construction and change tracking (<c>Add</c>,
/// <c>ChangeTracker.DetectChanges</c>) touch no database. No InMemory provider, no SQLite, no new
/// test package.
/// </remarks>
public class EventRemindersPersistenceTests
{
    private static FamilyHqDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FamilyHqDbContext>()
            .UseNpgsql("Host=localhost;Database=irrelevant")
            .ReplaceService<IModelCustomizer, NpgsqlModelCustomizer>()
            .Options;
        return new FamilyHqDbContext(options);
    }

    // ── C1 guard ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Overrides_DefaultCollection_IsNotFixedSize()
    {
        // `Overrides { get; init; } = []` on an IReadOnlyList<T> target compiles to
        // Array.Empty<EventReminder>() — fixed-size. EF's navigation fixup calls Add(...) on this
        // EXACT instance while materialising a TRACKED query of any row whose JSON holds one or
        // more overrides, and a fixed-size collection throws NotSupportedException the moment it
        // does ("Collection was of a fixed size."). That takes down every tracked read of an event
        // that has reminders: sync updates, kiosk edit, kiosk delete. A mutable List is required.
        var overrides = new EventReminders().Overrides;

        ((ICollection<EventReminder>)overrides).IsReadOnly.Should().BeFalse(
            "EF's navigation fixup mutates this collection on a tracked read; a fixed-size " +
            "Array.Empty<T>() throws NotSupportedException the instant fixup calls Add on it");
    }

    // ── C2 guard ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TwoEventsInheritingTheCalendarDefault_DoNotShareOneRemindersInstance()
    {
        // Reproduces the whole-branch review's finding: MapReminders returned the shared static
        // EventReminders.InheritsCalendarDefault for every useDefault:true event. EF owned types
        // (JSON-mapped included) cannot share one CLR instance across two owners — the second
        // Add used to throw InvalidOperationException ("...CalendarEventId is part of a key and so
        // cannot be modified"), and in the batch-save reconcile paths there was no throw at all:
        // EF silently re-parented the single instance onto the second owner and wrote the first
        // row's Reminders back as NULL.
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
            "EF owned types cannot share one CLR instance between two owners — each event must " +
            "get its own EventReminders, not a shared singleton");

        context.ChangeTracker.Entries<EventReminders>().Should().HaveCount(2,
            "each owner must have its own tracked EventReminders entry; one entry re-parented " +
            "onto the second owner is exactly how the first row silently lost its reminders");
    }
}

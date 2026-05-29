using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class RecurrenceInvariantsTests
{
    private static readonly DateTimeOffset OriginalStart = new(2026, 4, 6, 9, 0, 0, TimeSpan.Zero);

    private static CalendarEvent NewEvent() => new()
    {
        GoogleEventId = "evt-1",
        Title = "Test",
        Start = OriginalStart,
        End = OriginalStart.AddHours(1),
        OwnerCalendarInfoId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
    };

    [Fact]
    public void Validate_NonRecurringEvent_AllRecurrenceFieldsNull_DoesNotThrow()
    {
        var ev = NewEvent();

        var act = () => RecurrenceInvariants.Validate(ev);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_SeriesEvent_RecurringIdAndRuleSet_DoesNotThrow()
    {
        var ev = NewEvent();
        ev.GoogleRecurringEventId = "series-1";
        ev.RecurrenceRule = "RRULE:FREQ=WEEKLY";

        var act = () => RecurrenceInvariants.Validate(ev);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_ExceptionEvent_AllThreeFieldsSet_DoesNotThrow()
    {
        var ev = NewEvent();
        ev.GoogleRecurringEventId = "series-1";
        ev.RecurrenceRule = "RRULE:FREQ=WEEKLY";
        ev.OriginalStartTime = OriginalStart;

        var act = () => RecurrenceInvariants.Validate(ev);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_OriginalStartTimeSet_WithoutRecurringId_Throws()
    {
        var ev = NewEvent();
        ev.OriginalStartTime = OriginalStart;

        var act = () => RecurrenceInvariants.Validate(ev);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*OriginalStartTime*");
    }

    [Fact]
    public void Validate_RecurringIdSet_WithoutRecurrenceRule_Throws()
    {
        var ev = NewEvent();
        ev.GoogleRecurringEventId = "series-1";

        var act = () => RecurrenceInvariants.Validate(ev);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*RecurrenceRule*");
    }

    [Fact]
    public void Validate_RecurrenceRuleSet_WithoutRecurringId_DoesNotThrow()
    {
        // A rule with no series id violates neither invariant — pin this as
        // deliberately allowed so the contract can't silently tighten later.
        var ev = NewEvent();
        ev.RecurrenceRule = "RRULE:FREQ=WEEKLY";

        var act = () => RecurrenceInvariants.Validate(ev);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_NullEvent_ThrowsArgumentNull()
    {
        var act = () => RecurrenceInvariants.Validate(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}

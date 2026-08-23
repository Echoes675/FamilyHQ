using FamilyHQ.Core.Calendar;
using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

/// <summary>
/// FHQ-174 — the kiosk must build an all-day boundary the same way the sync does.
/// <para>
/// The modal used to stamp the BROWSER's UTC offset on every boundary it produced:
/// <c>new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt))</c>. For a timed event that is
/// right — the wall clock genuinely is in the viewer's zone. For an all-day event it is the same
/// substitution the sync path carried: on a kiosk at UTC+1 the day started at 23:00Z the day before,
/// which the <c>Start</c>/<c>End</c> EF converter kept and the outbound <c>"yyyy-MM-dd"</c> mapping
/// then reported to Google as the previous day. Left unfixed alongside the sync path it would have
/// been worse than the original bug: synced all-day rows at 00:00Z and kiosk-created ones at 23:00Z,
/// two representations of the same thing in one table.
/// </para>
///
/// <para><b>What these tests can and cannot do.</b> They document the contract. CI runs at a zero
/// host offset, so <c>TimeZoneInfo.Local.GetUtcOffset</c> there is <c>TimeSpan.Zero</c> and the old
/// code and the new agree on every value. The viewer offset is therefore passed in explicitly, which
/// keeps the function pure AND lets these tests exercise the non-zero offsets a real kiosk uses —
/// but only for the branch decision, not as proof that a regression would be caught. That is the
/// analyzer's job (<c>build/BannedSymbols.txt</c>).</para>
/// </summary>
public class EventModalAllDayBoundaryTests
{
    private static readonly DateTime PickedDay = new(2026, 6, 15);
    private static readonly TimeSpan KioskOffset = TimeSpan.FromHours(1);
    private static readonly TimeSpan WesternOffset = TimeSpan.FromHours(-5);

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(13)]
    public void ToModelInstant_AllDay_IsMidnightUtcWhateverTheViewersOffset(int offsetHours)
    {
        var instant = EventModalLogic.ToModelInstant(PickedDay, isAllDay: true, TimeSpan.FromHours(offsetHours));

        instant.Should().Be(new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ToModelInstant_AllDayWithALeftoverTimeOfDay_StillLandsOnMidnight()
    {
        // Toggling all-day on leaves the previous time of day on the model; the date pickers must
        // not carry it into the stored boundary.
        var instant = EventModalLogic.ToModelInstant(
            PickedDay.AddHours(9).AddMinutes(30), isAllDay: true, KioskOffset);

        instant.Should().Be(new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ToModelInstant_Timed_KeepsTheWallClockInTheViewersOffset()
    {
        // The FHQ-43/FHQ-170 behaviour that must NOT change: a timed event's wall clock is the
        // viewer's, and the offset is how the server knows which instant that is.
        var wallClock = PickedDay.AddHours(9);

        var instant = EventModalLogic.ToModelInstant(wallClock, isAllDay: false, KioskOffset);

        instant.Should().Be(new DateTimeOffset(2026, 6, 15, 9, 0, 0, KioskOffset));
        instant.DateTime.Should().Be(wallClock);
    }

    [Fact]
    public void ToPickerWallClock_AllDay_ShowsTheDayGoogleNamedEvenOnANegativeOffsetViewer()
    {
        // Reading a midnight-UTC boundary back as LOCAL time is how a west-of-UTC kiosk would show
        // the previous day in the date picker — and then write that day back on save. All-day
        // boundaries are read in UTC for the same reason they are written in UTC.
        var stored = new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);

        EventModalLogic.ToPickerWallClock(stored, isAllDay: true).Date.Should().Be(PickedDay);
    }

    [Fact]
    public void ToPickerWallClock_AllDayLegacyRowStoredAtANonZeroOffset_ReadsTheInstantsDay()
    {
        // A row written before this fix on a UTC+1 host: the wall clock says the 15th, the instant
        // says the 14th, and the instant is what the outbound mapping formats. The picker shows the
        // day the write path would actually send, so what the user sees is what Google will get.
        var legacy = new DateTimeOffset(2026, 6, 15, 0, 0, 0, KioskOffset);

        EventModalLogic.ToPickerWallClock(legacy, isAllDay: true).Date.Should().Be(new DateTime(2026, 6, 14));
    }

    [Fact]
    public void ToModelInstant_AllDayThenBack_RoundTripsTheDay()
    {
        var stored = EventModalLogic.ToModelInstant(PickedDay, isAllDay: true, WesternOffset);

        EventModalLogic.ToPickerWallClock(stored, isAllDay: true).Date.Should().Be(PickedDay);
    }

    // ── The toggle (FHQ-174) ──────────────────────────────────────────────────
    //
    // The gap the first cut left. ToModelInstant only runs when something re-derives a boundary, and
    // a picker setter is the only thing that used to. The ordinary create path never touches one:
    // tap a day cell (09:00 local), turn All Day on, save. Flipping IsAllDay re-routes the getters
    // but leaves the 09:00 instants in the model, so that path stored 08:00Z on a UTC+1 kiosk — the
    // day-shift hazard, on the most common route through the modal, and a contaminant in the very
    // audit that the existing-data decision reads. The toggle now re-derives both boundaries through
    // these two functions.

    [Fact]
    public void AllDayWallClocks_SingleDayEvent_IsThatDayAndTheExclusiveNextMidnight()
    {
        var (start, end) = EventModalLogic.AllDayWallClocks(PickedDay, PickedDay);

        start.Should().Be(PickedDay);
        end.Should().Be(PickedDay.AddDays(1));
    }

    [Fact]
    public void AllDayWallClocks_TimedEventBeingToggledOn_DropsTheTimeAndKeepsTheDays()
    {
        // What the create path actually hands over: 09:00–10:00 on the tapped day, read back off the
        // pickers as two dates.
        var (start, end) = EventModalLogic.AllDayWallClocks(PickedDay.AddHours(9), PickedDay.AddHours(10));

        start.Should().Be(PickedDay);
        end.Should().Be(PickedDay.AddDays(1));
    }

    [Fact]
    public void AllDayWallClocks_ThroughToModelInstant_IsMidnightUtcOnBothBoundaries()
    {
        // The property the whole ticket is about, asserted on the path the toggle takes.
        var (start, end) = EventModalLogic.AllDayWallClocks(PickedDay.AddHours(9), PickedDay.AddHours(10));

        EventModalLogic.ToModelInstant(start, isAllDay: true, KioskOffset)
            .Should().Be(new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero));
        EventModalLogic.ToModelInstant(end, isAllDay: true, KioskOffset)
            .Should().Be(new DateTimeOffset(2026, 6, 16, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void AllDayWallClocks_MultiDaySpan_KeepsTheLastDayInclusive()
    {
        var lastDay = PickedDay.AddDays(2);

        var (start, end) = EventModalLogic.AllDayWallClocks(PickedDay, lastDay);

        start.Should().Be(PickedDay);
        end.Should().Be(lastDay.AddDays(1), "Google's all-day end is the EXCLUSIVE next-day boundary");
    }

    [Fact]
    public void AllDayWallClocks_EndBeforeStart_CollapsesToASingleDay()
    {
        var (start, end) = EventModalLogic.AllDayWallClocks(PickedDay, PickedDay.AddDays(-3));

        start.Should().Be(PickedDay);
        end.Should().Be(PickedDay.AddDays(1));
    }

    [Fact]
    public void TimedWallClocks_RestoresTheTimesTheEventHadBeforeItWentAllDay()
    {
        // The documented choice for switching all-day OFF: keep the DATES the user can see, restore
        // the TIMES. Reading the stored all-day boundaries back would give 00:00–00:00.
        var (start, end) = EventModalLogic.TimedWallClocks(
            PickedDay, PickedDay, TimeSpan.FromHours(14), TimeSpan.FromHours(15).Add(TimeSpan.FromMinutes(30)));

        start.Should().Be(PickedDay.AddHours(14));
        end.Should().Be(PickedDay.AddHours(15).AddMinutes(30));
    }

    [Fact]
    public void TimedWallClocks_EventThatWasNeverTimed_LandsOnTheCreateDefault()
    {
        // An event that opened all-day has no times to restore, so it gets the same 09:00–10:00 slot
        // a freshly-created timed event gets — not 00:00–00:00.
        var (start, end) = EventModalLogic.TimedWallClocks(
            PickedDay, PickedDay, EventModalLogic.DefaultStartTimeOfDay, EventModalLogic.DefaultEndTimeOfDay);

        start.Should().Be(PickedDay.AddHours(9));
        end.Should().Be(PickedDay.AddHours(10));
    }

    [Fact]
    public void TimedWallClocks_MultiDayAllDayEvent_KeepsBothVisibleDates()
    {
        var lastDay = PickedDay.AddDays(2);

        var (start, end) = EventModalLogic.TimedWallClocks(
            PickedDay, lastDay, EventModalLogic.DefaultStartTimeOfDay, EventModalLogic.DefaultEndTimeOfDay);

        start.Should().Be(PickedDay.AddHours(9));
        end.Should().Be(lastDay.AddHours(10));
    }

    [Fact]
    public void TimedWallClocks_DerivedEndNotAfterStart_FallsBackToAnHour()
    {
        var (start, end) = EventModalLogic.TimedWallClocks(
            PickedDay, PickedDay, TimeSpan.FromHours(14), TimeSpan.FromHours(9));

        start.Should().Be(PickedDay.AddHours(14));
        end.Should().Be(PickedDay.AddHours(15));
    }

    [Fact]
    public void MinimumEnd_AllDay_StaysOnAMidnightUtcBoundaryAWholeDayLater()
    {
        // Dragging an all-day event's start past its end used to add ONE HOUR, which stored an
        // 01:00Z all-day End: a non-midnight boundary written by the fixed code, and a zero-length
        // day once the outbound "yyyy-MM-dd" formatting had run.
        var start = new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);

        var end = EventModalLogic.MinimumEnd(start, isAllDay: true);

        end.Should().Be(new DateTimeOffset(2026, 6, 16, 0, 0, 0, TimeSpan.Zero));
        GoogleAllDayDate.IsMidnightUtc(end).Should().BeTrue();
    }

    [Fact]
    public void MinimumEnd_Timed_KeepsTheOneHourFallback()
    {
        var start = new DateTimeOffset(2026, 6, 15, 9, 0, 0, KioskOffset);

        EventModalLogic.MinimumEnd(start, isAllDay: false)
            .Should().Be(new DateTimeOffset(2026, 6, 15, 10, 0, 0, KioskOffset));
    }

    [Fact]
    public void ToggleOnThenOff_RoundTripsATimedEventExactly()
    {
        // On → off with the times remembered is the sequence a user who taps the toggle twice sees.
        // It must return them to where they started, not to a default.
        var startWallClock = PickedDay.AddHours(9);
        var endWallClock = PickedDay.AddHours(10);

        var (allDayStart, allDayEnd) = EventModalLogic.AllDayWallClocks(startWallClock, endWallClock);
        var (timedStart, timedEnd) = EventModalLogic.TimedWallClocks(
            allDayStart, allDayEnd.AddTicks(-1).Date, startWallClock.TimeOfDay, endWallClock.TimeOfDay);

        timedStart.Should().Be(startWallClock);
        timedEnd.Should().Be(endWallClock);
    }
}

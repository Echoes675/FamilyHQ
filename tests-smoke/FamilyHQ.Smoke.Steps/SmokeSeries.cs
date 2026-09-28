using System.Globalization;
using FamilyHQ.Smoke.Common.Helpers;
using FamilyHQ.Smoke.Data.Models;
using FluentAssertions;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// Google's answer about a series, and the one comparison every recurrence scenario ends in.
/// <para>
/// Recurrence is checked against Google's <c>events.instances</c> and never against a calculation of
/// ours. A weekly rule and a locally-derived weekly rule agree in the common case and diverge at a
/// daylight-saving transition: that is not a difference of opinion, it is a latent bug with a date on it.
/// Whatever Google expands a rule to is, by definition, the occurrence set — so every scenario here asks
/// Google first and then asks preprod whether it agrees.
/// </para>
/// <para>
/// The assertions live in this one place rather than being written out in each step because they are the
/// same assertion every time, and a copy that drifted would quietly weaken whichever scenario held it.
/// </para>
/// </summary>
public static class SmokeSeries
{
    /// <summary>
    /// The bound every series in this suite carries. Three occurrences is enough to prove a step, to prove
    /// a two-weekday rule wraps into the next week, and to leave a middle occurrence to single out.
    /// </summary>
    public const int Occurrences = 3;

    /// <summary>
    /// The bound a yearly series carries. Two, because the third occurrence would be two years out — far
    /// enough that nobody would notice it sitting on a live calendar, and the second already proves the
    /// anchor survives a year.
    /// </summary>
    public const int YearlyOccurrences = 2;

    /// <summary>
    /// Waits until Google holds exactly <paramref name="expectedMasters"/> series master(s) carrying this
    /// scenario's marker, and returns them.
    /// <para>
    /// Masters, not instances: the listing asks Google <i>not</i> to expand, so what comes back is the
    /// events that carry a <c>recurrence</c> array. Two where one was expected means the series was
    /// written twice; one where two were expected means a split created no replacement.
    /// </para>
    /// </summary>
    public static async Task<IReadOnlyList<SmokeGoogleLocation>> WaitForMastersAsync(
        SmokeScenarioState state, int expectedMasters, string what, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        return await BoundedWait.ForAsync<IReadOnlyList<SmokeGoogleLocation>>(
            async () =>
            {
                var masters = (await MarkedInGoogleAsync(state, ct)).Where(IsMaster).ToList();
                return masters.Count == expectedMasters ? masters : null;
            },
            $"Google never settled on {expectedMasters} series master(s) carrying this scenario's "
            + $"correlation marker for {what}. More than expected means a series was written twice, or a "
            + "split left one behind; fewer means a series that should exist does not",
            TimeSpan.FromSeconds(state.Environment.Configuration.GoogleWaitSeconds));
    }

    /// <summary>
    /// Finds the series master(s) Google holds for this scenario and records them on the scenario, so a
    /// later step can ask Google to expand them.
    /// </summary>
    public static async Task<IReadOnlyList<SmokeGoogleLocation>> RecordMastersAsync(
        SmokeScenarioState state, int expectedMasters, string what, CancellationToken ct = default)
    {
        var found = await WaitForMastersAsync(state, expectedMasters, what, ct);

        state.SeriesMasters =
            [.. found.Select(location => new SmokeSeriesMaster(location.CalendarName, location.Event.Id))];

        return found;
    }

    /// <summary>
    /// The events Google holds for this scenario that are <b>exceptions</b> — single occurrences singled
    /// out of a series, each carrying the slot it came from in <c>originalStartTime</c>.
    /// <para>
    /// An exception is a separate event resource under the same series, so an unexpanded listing returns it
    /// alongside the master. Telling the two apart is what makes "one occurrence was edited and the rest
    /// were not" a checkable statement.
    /// </para>
    /// </summary>
    public static async Task<IReadOnlyList<GoogleEvent>> ExceptionsAsync(
        SmokeScenarioState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        return
        [
            .. (await MarkedInGoogleAsync(state, ct))
                .Select(location => location.Event)
                .Where(candidate => !IsRecurrenceMaster(candidate) && candidate.RecurringEventId is not null)
        ];
    }

    /// <summary>Everything Google holds for this scenario, unexpanded — masters, exceptions and single events alike.</summary>
    public static Task<IReadOnlyList<SmokeGoogleLocation>> MarkedInGoogleAsync(
        SmokeScenarioState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        return SmokeLookup.FindInGoogleAsync(
            state, state.EventDate, expandInstances: false, state.WindowDaysAfter, ct);
    }

    /// <summary>Google's own expansion of one master — the oracle for every occurrence assertion.</summary>
    public static async Task<IReadOnlyList<GoogleEvent>> InstancesAsync(
        SmokeScenarioState state,
        string calendarName,
        string masterId,
        DateOnly anchorDate,
        int daysAfter = SmokeLookup.DefaultWindowDaysAfter,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var calendarId = state.Environment.Calendars.RequireGoogleId(calendarName);
        var (from, to) = SmokeLookup.WindowAround(anchorDate, daysAfter);

        var instances = await state.Environment.Google.ListInstancesAsync(calendarId, masterId, from, to, ct);

        AssertOccurrencesStayInsideTheReservedBlock(state, instances);

        return instances;
    }

    /// <summary>
    /// Asserts that every occurrence Google expanded falls on a day this scenario reserved.
    /// <para>
    /// The one check that makes a mis-sized reservation impossible to ship quietly. A scenario that reserves
    /// a day and then writes a rule spanning a fortnight is not wrong <i>here</i> — it is wrong fourteen
    /// scenarios later, when another scenario's click lands on a tile this one left on its day, and the
    /// failure reads like a product fault. Asking Google where the occurrences actually fell turns
    /// that into an immediate failure, in the scenario that under-reserved, naming the span it needed.
    /// </para>
    /// <para>
    /// An occurrence past every day the allocator hands out is fine: a yearly series' second occurrence is a
    /// year from its first, so there is no scenario for it to collide with. That is why a yearly series
    /// reserves one day rather than a year of them.
    /// </para>
    /// </summary>
    private static void AssertOccurrencesStayInsideTheReservedBlock(
        SmokeScenarioState state, IReadOnlyList<GoogleEvent> instances)
    {
        // The daylight-saving scenarios reserve nothing: their dates are the clocks', not the allocator's.
        if (state.ReservedDays is not { } block)
        {
            return;
        }

        var beyondEverything = SmokeScenarioDays.BeyondEveryAllocatableDay(
            state.Environment.Configuration.SyncHorizonDays);

        var strays = instances
            .Select(DateOf)
            .Where(date => !block.Covers(date) && date < beyondEverything)
            .Distinct()
            .OrderBy(date => date)
            .ToList();

        strays.Should().BeEmpty(
            $"every occurrence of this scenario's series has to fall inside the {block.Days} day(s) it "
            + $"reserved ({block}), and these do not: "
            + $"{string.Join(", ", strays.Select(date => date.ToString("yyyy-MM-dd")))}. A day outside the "
            + "block belongs to another scenario, and an event left there is what stops that scenario's "
            + "click from landing. Reserve the span the rule actually covers — SmokeScenarioDays has a "
            + "factory for each shape the suite writes");
    }

    /// <summary>
    /// Google's expansion of every series master this scenario has recorded, in start order.
    /// <para>
    /// A union, because a "this and following" change leaves the family with two series and one calendar:
    /// what they see is the truncated original's occurrences followed by the replacement's, and the
    /// question preprod has to answer right is about that whole set, not about either half.
    /// </para>
    /// </summary>
    public static async Task<IReadOnlyList<GoogleEvent>> InstancesForScenarioAsync(
        SmokeScenarioState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.SeriesMasters.Should().NotBeEmpty(
            "no step in this scenario recorded the series master Google holds, so there is no expansion to "
            + "compare against. A recurrence scenario has to find its master in Google before asserting "
            + "anything about its occurrences");

        var instances = new List<GoogleEvent>();
        foreach (var master in state.SeriesMasters)
        {
            instances.AddRange(
                await InstancesAsync(
                    state, master.CalendarName, master.GoogleEventId, state.EventDate,
                    state.WindowDaysAfter, ct));
        }

        return [.. instances.OrderBy(StartOf)];
    }

    /// <summary>
    /// The one series master this scenario has, read from Google as it stands right now.
    /// <para>
    /// A fresh read rather than the copy a seeding step kept, because the assertions that use it are about
    /// what the system of record holds <i>after</i> an edit.
    /// </para>
    /// </summary>
    public static async Task<GoogleEvent> MasterEventAsync(
        SmokeScenarioState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var master = state.SeriesMasters.Should().ContainSingle(
            "this step is about one series; a scenario that has split one into two has to say which half it "
            + "means").Subject;

        var calendarId = state.Environment.Calendars.RequireGoogleId(master.CalendarName);
        return await state.Environment.Google.GetEventAsync(calendarId, master.GoogleEventId, ct);
    }

    /// <summary>
    /// The single <c>RRULE</c> line a master carries.
    /// <para>
    /// Exactly one, because a second would change what the series means and Google would expand the pair,
    /// not the first of them.
    /// </para>
    /// </summary>
    public static string RequireSingleRule(GoogleEvent master)
    {
        ArgumentNullException.ThrowIfNull(master);

        var rules = (master.Recurrence ?? [])
            .Where(line => line.StartsWith("RRULE:", StringComparison.Ordinal))
            .ToList();

        rules.Should().ContainSingle(
            "a series master carries exactly one RRULE; none means the rule was lost and the event went "
            + "out as a single occurrence, and a second would change what the series means");

        return rules[0];
    }

    /// <summary>
    /// The instant an event starts, whether Google described it as a time or as a calendar date.
    /// <para>
    /// An all-day date resolves to midnight UTC, which is the representation FamilyHQ stores for one — so
    /// the two sides are comparable without either being re-interpreted through a zone neither of them
    /// sent.
    /// </para>
    /// </summary>
    public static DateTimeOffset StartOf(GoogleEvent googleEvent)
    {
        ArgumentNullException.ThrowIfNull(googleEvent);

        return InstantOf(googleEvent.Start, googleEvent.Id, "start");
    }

    /// <summary>The instant an event ends, on the same terms as <see cref="StartOf"/>.</summary>
    public static DateTimeOffset EndOf(GoogleEvent googleEvent)
    {
        ArgumentNullException.ThrowIfNull(googleEvent);

        return InstantOf(googleEvent.End, googleEvent.Id, "end");
    }

    /// <summary>
    /// The slot of the series an exception replaces — Google's <c>originalStartTime</c>.
    /// <para>
    /// The one field that makes an exception an exception. Without it the series would expand its own
    /// occurrence in that slot as well, and the family would see both the edited event and the one it was
    /// supposed to replace.
    /// </para>
    /// </summary>
    public static DateTimeOffset OriginalSlotOf(GoogleEvent exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return InstantOf(exception.OriginalStartTime, exception.Id, "originalStartTime");
    }

    /// <summary>The date, in the family's zone, an occurrence falls on.</summary>
    public static DateOnly DateOf(GoogleEvent googleEvent) =>
        DateOnly.FromDateTime(FamilyClock.ToFamilyWallClock(StartOf(googleEvent)));

    /// <summary>An instant as the family would read it off the kiosk — the wall clock, to the minute.</summary>
    public static string WallClock(DateTimeOffset instant) =>
        FamilyClock.ToFamilyWallClock(instant).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// Asserts that the occurrences preprod serves fall on exactly the instants Google expands the rule to,
    /// and returns them.
    /// <para>
    /// The months preprod is asked about are derived from Google's instances plus a month either side, so a
    /// yearly series is not missed and an unexpected extra occurrence near a real one is still found.
    /// </para>
    /// </summary>
    public static Task<IReadOnlyList<PreprodEvent>> AssertPreprodServesGooglesInstancesAsync(
        SmokeScenarioState state,
        IReadOnlyList<GoogleEvent> instances,
        string what,
        CancellationToken ct = default) =>
        AssertPreprodAgreesWithGoogleAsync(state, instances, what, compareTitles: false, ct);

    /// <summary>
    /// The same, extended to the title on each occurrence — for the scenarios whose point is that some
    /// occurrences changed and others did not.
    /// </summary>
    public static Task<IReadOnlyList<PreprodEvent>> AssertPreprodAgreesWithGoogleTitleByTitleAsync(
        SmokeScenarioState state,
        IReadOnlyList<GoogleEvent> instances,
        string what,
        CancellationToken ct = default) =>
        AssertPreprodAgreesWithGoogleAsync(state, instances, what, compareTitles: true, ct);

    /// <summary>
    /// The occurrences of <paramref name="instances"/> that fall inside the window FamilyHQ syncs, which is
    /// the only part of Google's expansion the kiosk can be expected to show.
    /// </summary>

    public static IReadOnlyList<GoogleEvent> WithinSyncHorizon(
        SmokeScenarioState state, IReadOnlyList<GoogleEvent> instances)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(instances);

        var horizon = DateTimeOffset.UtcNow.AddDays(state.Environment.Configuration.SyncHorizonDays);
        return [.. instances.Where(instance => StartOf(instance) < horizon)];
    }

    /// <summary>
    /// Waits, once and boundedly, until preprod's occurrence set agrees with Google's expansion, and returns
    /// it.
    /// <para>
    /// <b>Every part of the comparison is inside the wait, and that is the point.</b> A change made in Google
    /// often leaves the <i>number</i> of occurrences alone — a renamed series, a moved occurrence — so a wait
    /// that settled on the count and then compared the rest would return the instant it was asked and compare
    /// against a kiosk the push had not reached yet. It would fail against a perfectly good environment, and
    /// the obvious-looking fix (wait a bit longer, then look again) is exactly the compensation this suite
    /// forbids. Waiting on the whole comparison makes the deadline mean what it should: how long the live path
    /// is allowed to take.
    /// </para>
    /// <para>
    /// When the deadline does expire, one further <b>read-only</b> look produces the itemised difference,
    /// because "the condition was still false after three minutes" does not say which occurrence disagreed. If
    /// that look somehow agrees, the original timeout is raised rather than swallowed.
    /// </para>
    /// </summary>
    private static async Task<IReadOnlyList<PreprodEvent>> AssertPreprodAgreesWithGoogleAsync(
        SmokeScenarioState state,
        IReadOnlyList<GoogleEvent> instances,
        string what,
        bool compareTitles,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(instances);

        instances.Should().NotBeEmpty(
            $"Google expands {what} to no occurrences at all, so there is nothing for preprod to agree or "
            + "disagree with — the series this scenario believes it created is not there");

        // Bounded by how far FamilyHQ syncs. An occurrence past that is legitimately not on the kiosk, so
        // including it would report the designed edge of the sync window as a disagreement with Google. Only
        // a yearly series reaches that far; for everything else this changes nothing.
        var comparable = WithinSyncHorizon(state, instances);

        comparable.Should().NotBeEmpty(
            "every occurrence Google expands "
            + $"{what} to falls beyond the {state.Environment.Configuration.SyncHorizonDays} days preprod "
            + "syncs, so the scenario is comparing nothing at all. Move the series closer");

        var expectedStarts = OrderedStarts(comparable);
        var expectedTitles = TitlesByWallClock(comparable);
        var months = SmokeLookup.MonthsCovering(comparable.Select(DateOf));

        try
        {
            return await SmokeLookup.WaitForPreprodAsync(
                state,
                months,
                candidates => OrderedStarts(candidates).SequenceEqual(expectedStarts)
                              && (!compareTitles || AgreesOnTitles(expectedTitles, candidates)),
                $"preprod never came to agree with Google's expansion of {what}. Google is the oracle: "
                + $"it expands the rule to {expectedStarts.Count} occurrence(s)"
                + (compareTitles ? ", each with a title of its own" : string.Empty)
                + ". A set that never converges means the two disagree about what the rule means, which is a "
                + "latent bug with a date on it, or that the change never travelled the live push path at all",
                ct);
        }
        catch (TimeoutException)
        {
            await ReportTheDifferenceAsync(state, months, comparable, what, compareTitles, ct);
            throw;
        }
    }

    /// <summary>
    /// Re-reads preprod once, after the deadline, purely to turn "still false" into a named difference. Raises
    /// the itemised failure; returns normally only if preprod now agrees, in which case the caller rethrows the
    /// timeout rather than letting a late arrival pass for a timely one.
    /// </summary>
    private static async Task ReportTheDifferenceAsync(
        SmokeScenarioState state,
        IReadOnlyList<DateOnly> months,
        IReadOnlyList<GoogleEvent> comparable,
        string what,
        bool compareTitles,
        CancellationToken ct)
    {
        var served = await SmokeLookup.FindInPreprodAsync(state, months, ct);

        OrderedStarts(served).Should().Equal(
            OrderedStarts(comparable),
            $"every occurrence of {what} must fall on the instant Google says it does — not merely the right "
            + "number of them on roughly the right days");

        if (!compareTitles)
        {
            return;
        }

        TitlesByWallClock(served).Should().BeEquivalentTo(
            TitlesByWallClock(comparable),
            $"the kiosk must show, for {what}, the title Google holds against each occurrence. Keyed on when "
            + "the occurrence falls rather than on its position, because \"two of them changed and one did "
            + "not\" is the outcome these scenarios are about, and a positional comparison would report the "
            + "right failure against the wrong occurrence");
    }

    private static List<DateTime> OrderedStarts(IEnumerable<GoogleEvent> instances) =>
        [.. instances.Select(instance => StartOf(instance).UtcDateTime).OrderBy(instant => instant)];

    private static List<DateTime> OrderedStarts(IEnumerable<PreprodEvent> served) =>
        [.. served.Select(occurrence => occurrence.Start.UtcDateTime).OrderBy(instant => instant)];

    private static Dictionary<string, string?> TitlesByWallClock(IEnumerable<GoogleEvent> instances) =>
        instances.ToDictionary(instance => WallClock(StartOf(instance)), instance => instance.Summary);

    private static Dictionary<string, string?> TitlesByWallClock(IEnumerable<PreprodEvent> served) =>
        served.ToDictionary(occurrence => WallClock(occurrence.Start), occurrence => (string?)occurrence.Title);

    private static bool AgreesOnTitles(
        Dictionary<string, string?> expected, IReadOnlyList<PreprodEvent> served) =>
        served.Count == expected.Count
        && served.All(occurrence =>
            expected.TryGetValue(WallClock(occurrence.Start), out var title)
            && string.Equals(title, occurrence.Title, StringComparison.Ordinal));

    /// <summary>True when the event Google returned is a series master — i.e. it carries the rule.</summary>
    public static bool IsRecurrenceMaster(GoogleEvent googleEvent)
    {
        ArgumentNullException.ThrowIfNull(googleEvent);

        return googleEvent.Recurrence?.Count > 0;
    }

    private static bool IsMaster(SmokeGoogleLocation location) => IsRecurrenceMaster(location.Event);

    private static DateTimeOffset InstantOf(GoogleEventDateTime? boundary, string eventId, string which)
    {
        if (boundary?.DateTime is { } instant)
        {
            return instant;
        }

        if (boundary?.Date is { } date)
        {
            return AllDayInstant(date, eventId, which);
        }

        throw new InvalidOperationException(
            $"Google's event {eventId} has neither a dateTime nor a date for its {which}, so there is "
            + "nothing to compare against.");
    }

    private static DateTimeOffset AllDayInstant(string date, string eventId, string boundary)
    {
        if (!DateTimeOffset.TryParseExact(
                date,
                SmokeEventShape.GoogleDateFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            throw new InvalidOperationException(
                $"Google's event {eventId} carries an all-day {boundary} date that is not an RFC 3339 "
                + $"full-date (length {date.Length}).");
        }

        return parsed;
    }
}

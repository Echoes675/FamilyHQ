using FamilyHQ.Smoke.Common.Helpers;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// Hands each scenario a run of days of its own to put its events on.
/// <para>
/// <b>Why a scenario needs one.</b> The suite shares one live environment, one Google account and one set of
/// calendars. Put two scenarios' events at the same time on the same day and the kiosk's day view ends up
/// with tiles competing for one column: the tile a scenario wants is found but another scenario's tile sits
/// over it, and the click never lands. That is not a product fault and not a flake — it is the suite crowding
/// itself out, and it gets worse with every scenario added.
/// </para>
/// <para>
/// <b>Why a block and not a day.</b> A scenario occupies every day its events fall on, which for a series is
/// not the day it starts on. Handing out one day each while a weekly series of three spans a fortnight put
/// scenario N's second and third occurrences squarely on the days scenarios N+7 and N+14 had been given — and
/// that is what broke the pre-prod gate on its first real use. So a scenario reserves the span it
/// actually needs, stated through one of the factories below, and the allocator hands out the next block that
/// nothing else has.
/// </para>
/// <para>
/// <b>Why a counter and not a hash of the scenario's id.</b> A hash spreads events unevenly: with a dozen
/// scenarios over even a couple of months' worth of days, two landing on the same day is more likely than not.
/// A counter cannot collide. It does mean a scenario run on its own sits on a different day than it would in a
/// full run, which is harmless — nothing about a scenario depends on which day it uses, and its events are
/// found by the correlation id and the short title id, never by their date.
/// </para>
/// <para>
/// Allocation starts tomorrow rather than today, so a run that begins at 23:55 does not put an event on a day
/// the kiosk is about to roll off.
/// </para>
/// </summary>
public static class SmokeScenarioDays
{
    /// <summary>The first offset from today that is allocated.</summary>
    private const int FirstOffset = 1;

    /// <summary>
    /// Days of the sync horizon deliberately left unallocated. The horizon is where preprod stops syncing,
    /// so a block that ended on it would put occurrences the kiosk is never going to show inside a scenario
    /// that asserts on them.
    /// </summary>
    private const int HorizonHeadroomDays = 14;

    /// <summary>
    /// The allowance a scenario adds when its rule names a weekday, because its first occurrence is then the
    /// first such weekday on or after the day it was given — up to six days later than the block starts.
    /// </summary>
    private const int WeekdayShiftAllowance = 6;

    private static readonly object Gate = new();

    private static int _nextOffset = FirstOffset;

    /// <summary>The span a scenario that creates one event on one day needs.</summary>
    public const int SingleDay = 1;

    /// <summary>
    /// The span a yearly series needs <i>locally</i>: one day. Its second occurrence is a whole year out,
    /// which is past every day this allocator will ever hand out, so it cannot land on another scenario's
    /// block — see <see cref="BeyondEveryAllocatableDay"/>.
    /// </summary>
    public const int YearlySeries = SingleDay;

    /// <summary>The span a daily series of <paramref name="occurrences"/> covers.</summary>
    public static int Daily(int occurrences) => Bounded(occurrences);

    /// <summary>The span a weekly series of <paramref name="occurrences"/> covers.</summary>
    public static int Weekly(int occurrences) => EveryNWeeks(1, occurrences);

    /// <summary>
    /// The span a series repeating every <paramref name="intervalWeeks"/> weeks covers, over
    /// <paramref name="occurrences"/> occurrences.
    /// </summary>
    public static int EveryNWeeks(int intervalWeeks, int occurrences)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(intervalWeeks, 1);

        return 1 + (7 * intervalWeeks * (Bounded(occurrences) - 1));
    }

    /// <summary>
    /// The same span, widened for a scenario whose first occurrence is pushed forward to a named weekday.
    /// <para>
    /// Stated rather than folded into the factories because only some scenarios do it, and a scenario that
    /// shifts its anchor without widening its block is one whose last occurrence sits on the next scenario's
    /// first day.
    /// </para>
    /// </summary>
    public static int PlusWeekdayShift(int days) => days + WeekdayShiftAllowance;

    /// <summary>
    /// The next block of <paramref name="days"/> consecutive days that no scenario has been given.
    /// <para>
    /// Blocks never overlap, and none of them touches the days the daylight-saving scenarios need: those
    /// cannot be moved — the whole point of them is the date the clocks change — so the allocator works
    /// around them instead.
    /// </para>
    /// </summary>
    /// <param name="days">How many consecutive days the scenario needs, from one of the factories above.</param>
    /// <param name="syncHorizonDays">
    /// How far ahead preprod syncs, from <c>Smoke__SyncHorizonDays</c>. The budget every block comes out of:
    /// a block past it holds occurrences the kiosk is not expected to show.
    /// </param>
    public static SmokeDayBlock Reserve(int days, int syncHorizonDays)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);

        var today = FamilyClock.Today;

        lock (Gate)
        {
            var offset = OffsetClearOfTheClockChange(_nextOffset, days, today);
            var lastOffset = offset + days - 1;
            var budget = LastAllocatableOffset(syncHorizonDays);

            if (lastOffset > budget)
            {
                throw new InvalidOperationException(
                    $"The smoke suite has run out of days: this scenario asked for {days} and the only ones "
                    + $"left are day {offset} to day {budget} after today. Every scenario needs a run of days "
                    + "nothing else is using, and they all have to fit inside the "
                    + $"{syncHorizonDays} days preprod syncs. Reclaim room rather than widening the horizon: "
                    + "the cheapest room is a scenario reserving more days than its events actually cover.");
            }

            _nextOffset = lastOffset + 1;
            return new SmokeDayBlock(today.AddDays(offset), days);
        }
    }

    /// <summary>
    /// The last offset from today a block may end on. Anything past it is outside what preprod syncs, give
    /// or take the headroom.
    /// </summary>
    public static int LastAllocatableOffset(int syncHorizonDays) => syncHorizonDays - HorizonHeadroomDays;

    /// <summary>
    /// The first day no block can reach, so an occurrence on or after it cannot collide with any scenario.
    /// <para>
    /// This is what makes a yearly series safe to reserve a single day for: its second occurrence is a year
    /// past its first, and a year is past everything the allocator hands out.
    /// </para>
    /// </summary>
    public static DateOnly BeyondEveryAllocatableDay(int syncHorizonDays) =>
        FamilyClock.Today.AddDays(LastAllocatableOffset(syncHorizonDays) + 1);

    /// <summary>
    /// <paramref name="offset"/>, moved past the daylight-saving scenarios' days if a block of
    /// <paramref name="days"/> starting there would overlap them.
    /// <para>
    /// The daylight-saving scenarios place a three-day series on the day before the next transition, the day
    /// of it, and the day after, and they are the only scenarios in the suite whose dates are not handed out
    /// here — they cannot be, because a transition happens when it happens. Reserving those three days for
    /// them is cheaper than the alternative, which is an allocated scenario's tiles sitting under theirs.
    /// </para>
    /// </summary>
    private static int OffsetClearOfTheClockChange(int offset, int days, DateOnly today)
    {
        if (ClockChangeDate() is not { } change)
        {
            return offset;
        }

        var firstReserved = change.AddDays(-1).DayNumber - today.DayNumber;
        var lastReserved = change.AddDays(1).DayNumber - today.DayNumber;

        var overlaps = offset <= lastReserved && offset + days - 1 >= firstReserved;
        return overlaps ? lastReserved + 1 : offset;
    }

    /// <summary>
    /// The next date the family's zone changes its UTC offset, or null for a zone that has no transition in
    /// the coming year.
    /// <para>
    /// Null rather than a failure: a zone without daylight saving has nothing for the allocator to work
    /// around, and it is not this class's business to report it. The daylight-saving scenarios ask the same
    /// question and fail loudly on the same answer, which is where that condition belongs.
    /// </para>
    /// </summary>
    private static DateOnly? ClockChangeDate()
    {
        try
        {
            return FamilyClock.NextOffsetChangeDate();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static int Bounded(int occurrences)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(occurrences, 1);

        return occurrences;
    }
}

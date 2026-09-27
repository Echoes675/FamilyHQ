using FamilyHQ.Smoke.Common.Helpers;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// Hands each scenario a day of its own to put its events on.
/// <para>
/// <b>Why a scenario needs one.</b> The suite shares one live environment, one Google account and one set of
/// calendars, and its events are kept. Put every scenario's event at the same time on the same day and the
/// kiosk's day view ends up with a dozen tiles competing for one column: the tile a scenario wants is found
/// but another scenario's tile sits over it, and the click never lands. That is not a product fault and not a
/// flake — it is the suite crowding itself out, and it gets worse with every scenario added.
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

    private static int _allocated;

    /// <summary>The next unused day, in the family's zone.</summary>
    public static DateOnly Next() =>
        FamilyClock.Today.AddDays(FirstOffset + Interlocked.Increment(ref _allocated) - 1);
}

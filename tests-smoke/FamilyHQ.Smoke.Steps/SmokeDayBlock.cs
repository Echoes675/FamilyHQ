namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// The run of days one scenario has to itself, as <see cref="SmokeScenarioDays"/> handed it out.
/// <para>
/// A block rather than a single day because a series occupies more than the day it starts on: a weekly
/// series of three covers a fortnight, and a fortnightly one covers a month. Reserving the day and
/// forgetting the span is what let one scenario's later occurrences land on the day a scenario fourteen
/// places behind it was given.
/// </para>
/// </summary>
/// <param name="FirstDay">The first day of the block — where a scenario's first event goes.</param>
/// <param name="Days">How many consecutive days the block covers, the first one included.</param>
public sealed record SmokeDayBlock(DateOnly FirstDay, int Days)
{
    /// <summary>The last day the block covers, inclusive.</summary>
    public DateOnly LastDay => FirstDay.AddDays(Days - 1);

    /// <summary>True when <paramref name="date"/> falls inside this block.</summary>
    public bool Covers(DateOnly date) => date >= FirstDay && date <= LastDay;

    public override string ToString() =>
        Days == 1 ? $"{FirstDay:yyyy-MM-dd}" : $"{FirstDay:yyyy-MM-dd}..{LastDay:yyyy-MM-dd}";
}

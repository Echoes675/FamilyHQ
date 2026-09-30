namespace FamilyHQ.Smoke.Common.Pages;

/// <summary>The units the kiosk's reminder picker counts an offset in.</summary>
public enum SmokeReminderUnit
{
    Minutes,
    Hours,
    Days,
    Weeks
}

/// <summary>
/// One reminder, as a smoke scenario states it and as the kiosk's picker takes it: an amount, a unit and
/// a delivery method.
/// <para>
/// Google stores only the <see cref="Minutes"/>, so that is what every comparison against Google is made
/// on. The amount and unit exist because the picker is driven in them — "two hours before" is two taps and
/// a typed <c>2</c>, not a typed <c>120</c> — and a scenario that stated 120 minutes while the family would
/// have chosen two hours would be exercising a path nobody uses.
/// </para>
/// </summary>
/// <param name="Method">Google's delivery method: <c>popup</c> or <c>email</c>.</param>
/// <param name="Amount">How many <paramref name="Unit"/> before the event starts.</param>
/// <param name="Unit">The unit the amount is counted in.</param>
public sealed record SmokeReminder(string Method, int Amount, SmokeReminderUnit Unit)
{
    /// <summary>Google's delivery method for an on-screen notification.</summary>
    public const string PopupMethod = "popup";

    /// <summary>Google's delivery method for an emailed reminder.</summary>
    public const string EmailMethod = "email";

    /// <summary>A notification <paramref name="amount"/> <paramref name="unit"/> before the event starts.</summary>
    public static SmokeReminder Popup(int amount, SmokeReminderUnit unit) => new(PopupMethod, amount, unit);

    /// <summary>An email <paramref name="amount"/> <paramref name="unit"/> before the event starts.</summary>
    public static SmokeReminder Email(int amount, SmokeReminderUnit unit) => new(EmailMethod, amount, unit);

    /// <summary>
    /// The same reminder as Google records it — whole minutes before the event starts — for a row read back
    /// off the screen, which carries the offset and not the unit the family chose.
    /// </summary>
    public static SmokeReminder FromMinutes(string method, int minutes) =>
        new(method, minutes, SmokeReminderUnit.Minutes);

    /// <summary>The offset in minutes, which is the only shape Google holds.</summary>
    public int Minutes => Amount * UnitMinutes(Unit);

    /// <summary>The picker's <c>data-testid</c> suffix for <paramref name="unit"/>'s pill.</summary>
    public static string UnitTestIdSuffix(SmokeReminderUnit unit) =>
        unit.ToString().ToLowerInvariant();

    /// <summary>
    /// The pair every assertion is made on. Comparing whole records would make "2 hours" and
    /// "120 minutes" different reminders, which Google does not agree with.
    /// </summary>
    public (string Method, int Minutes) AsGoogleHoldsIt => (Method, Minutes);

    private static int UnitMinutes(SmokeReminderUnit unit) => unit switch
    {
        SmokeReminderUnit.Minutes => 1,
        SmokeReminderUnit.Hours => 60,
        SmokeReminderUnit.Days => 24 * 60,
        SmokeReminderUnit.Weeks => 7 * 24 * 60,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Not a reminder unit.")
    };
}

namespace FamilyHQ.Simulator.Google;

using FamilyHQ.Simulator.DTOs;

/// <summary>
/// Google Calendar's real behaviour for an event's <c>reminders</c>, in one place.
/// </summary>
/// <remarks>
/// Every rule here was observed against the live API, and several are surprising enough to be worth
/// stating plainly: Google mostly does NOT reject a bad reminder. It accepts the request, returns
/// <c>200</c>, and silently stores something different — clamping an out-of-range offset,
/// de-duplicating, and dropping a delivery method it does not recognise. Only two shapes are
/// rejected outright.
/// <para>
/// This matters for a test double specifically. Rejecting input that Google accepts would be the
/// more "correct-looking" simulator and the more dangerous one: the kiosk's tests would then assert
/// an error path that production never takes, and the silent-rewrite path — where a caller believes
/// it set a reminder that does not exist — would go untested.
/// </para>
/// </remarks>
public static class ReminderSemantics
{
    /// <summary>The reason Google gives for a sixth override.</summary>
    private const string CountExceededReason = "eventRemindersCountExceedsLimit";

    /// <summary>The reason Google gives for asking for the defaults and for specific overrides at once.</summary>
    private const string UseDefaultConflictReason = "cannotUseDefaultRemindersAndSpecifyOverride";

    /// <summary>Google's cap on overrides per event.</summary>
    private const int MaxOverrides = 5;

    /// <summary>Four weeks, in minutes: the largest offset Google will store.</summary>
    private const int MaxMinutes = 40320;

    /// <summary>The only delivery methods Google recognises. Anything else is dropped, not rejected.</summary>
    private static readonly string[] KnownMethods = ["popup", "email"];

    /// <summary>
    /// The Google error <c>reason</c> this request would be rejected with, or <c>null</c> if Google
    /// would accept it (possibly after rewriting it — see <see cref="NormaliseForStorage"/>).
    /// </summary>
    /// <remarks>
    /// Call this on the request <b>as sent</b>, before normalising. Google counts the overrides it
    /// received: six that de-duplicate to five are still rejected.
    /// </remarks>
    public static string? Validate(GoogleEventReminders? reminders)
    {
        if (reminders is null)
            return null;

        var overrides = reminders.Overrides ?? [];

        if (overrides.Count > MaxOverrides)
            return CountExceededReason;

        // "Use the calendar's defaults" and "use exactly these" are mutually exclusive. An empty
        // array alongside useDefault:true is fine, and is how a client reverts to the default —
        // useDefault:true on its own is rejected, so the empty array carries real meaning.
        if (reminders.UseDefault == true && overrides.Count > 0)
            return UseDefaultConflictReason;

        return null;
    }

    /// <summary>
    /// The <c>message</c> Google sends alongside a rejection reason, verbatim as the live API sends it.
    /// </summary>
    /// <remarks>
    /// The message lives here rather than at the call site so that the reason and the wording a client
    /// may show a user cannot drift apart. An unrecognised reason is a programming error, not a
    /// rejection Google has.
    /// </remarks>
    public static string RejectionMessage(string reason) => reason switch
    {
        CountExceededReason => "The event exceeds the allowed maximum number of reminders.",
        UseDefaultConflictReason => "Cannot specify both default reminders and overrides at the same time.",
        _ => throw new ArgumentOutOfRangeException(
            nameof(reason), reason, "Not a reminder rejection reason Google gives.")
    };

    /// <summary>
    /// What Google actually stores for an accepted request: offsets clamped into range, unknown
    /// delivery methods dropped, duplicates collapsed.
    /// </summary>
    public static GoogleEventReminders? NormaliseForStorage(GoogleEventReminders? reminders)
    {
        if (reminders is null)
            return null;

        if (reminders.Overrides is null)
            return reminders;

        var normalised = reminders.Overrides
            // An unrecognised method is dropped silently, which can leave an event with no reminders
            // at all while the write still reports success.
            .Where(o => o.Method is not null && KnownMethods.Contains(o.Method))
            // Clamp BEFORE de-duplicating: two different out-of-range offsets can clamp onto the same
            // reminder, and Google stores one.
            .Select(o => new GoogleEventReminderOverride(o.Method, Clamp(o.Minutes)))
            .DistinctBy(o => (o.Method, o.Minutes))
            .ToList();

        return reminders with { Overrides = normalised };
    }

    /// <summary>
    /// What a read returns for an event, given what is stored, whether the event is all-day, and the
    /// owning calendar's defaults.
    /// </summary>
    /// <remarks>
    /// Three behaviours are folded in here, all observed:
    /// <list type="bullet">
    /// <item><description>Nothing stored on a <b>timed</b> event reads as "follows the calendar
    /// default".</description></item>
    /// <item><description>Nothing stored on an <b>all-day</b> event reads as the calendar's defaults
    /// copied onto it as explicit overrides. An all-day event never inherits, so useDefault:true does
    /// not occur on one.</description></item>
    /// <item><description>An empty override list is returned as a <b>missing key</b>, never as an
    /// empty array, and the surviving overrides come back in an order unrelated to the order they
    /// were sent in.</description></item>
    /// </list>
    /// </remarks>
    public static GoogleEventReminders ShapeForRead(
        GoogleEventReminders? stored,
        bool isAllDay,
        IReadOnlyList<GoogleEventReminderOverride>? calendarDefaults)
    {
        if (stored is null)
        {
            return isAllDay
                ? new GoogleEventReminders(UseDefault: false, Overrides: Reorder(calendarDefaults))
                : new GoogleEventReminders(UseDefault: true, Overrides: null);
        }

        return stored with { Overrides = Reorder(stored.Overrides) };
    }

    private static int? Clamp(int? minutes) =>
        minutes is null ? null : Math.Clamp(minutes.Value, 0, MaxMinutes);

    /// <summary>
    /// Returns the overrides in an order deliberately unrelated to the order they arrived in, or
    /// <c>null</c> when there are none — Google omits the key rather than sending an empty array.
    /// </summary>
    /// <remarks>
    /// Largest offset first. Google's own ordering is not documented and need not be reproduced; what
    /// must be reproduced is that send order is <b>not</b> preserved, so anything depending on it
    /// fails here rather than in production. A deterministic reorder does that without making the
    /// suite flaky.
    /// </remarks>
    private static List<GoogleEventReminderOverride>? Reorder(
        IReadOnlyList<GoogleEventReminderOverride>? overrides) =>
        overrides is null || overrides.Count == 0
            ? null
            : [.. overrides.OrderByDescending(o => o.Minutes).ThenBy(o => o.Method)];
}

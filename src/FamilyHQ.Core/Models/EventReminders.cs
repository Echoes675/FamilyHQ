namespace FamilyHQ.Core.Models;

/// <summary>
/// One of Google's <c>reminders.overrides</c> entries, stored exactly as Google sent it.
/// </summary>
/// <param name="Method">
/// Google's delivery method — <c>"popup"</c> or <c>"email"</c> in practice. Deliberately a STRING,
/// not an enum: an unknown method must round-trip rather than fail to deserialise or be silently
/// dropped. FHQ-193 found Google itself drops an unknown method on a WRITE (with a 200), which is
/// all the more reason not to add a second place where a value can vanish.
/// </param>
/// <param name="Minutes">
/// Minutes before the event's start (for an all-day event, before local midnight on its first day).
/// SIGNED on purpose. Google's schema declares no minimum — the 0–40320 range is prose only — and a
/// reminder that fires AFTER the start is representable in Google's UI though not in its API
/// (FHQ-193 §10). Storing it signed means an unexpected value round-trips instead of being "fixed".
/// </param>
public sealed record EventReminder(string Method, int Minutes);

/// <summary>
/// Google's <c>reminders</c> object for one event (or a calendar's defaults), in Google's own shape.
/// Immutable: the read path builds one and never mutates it.
/// </summary>
/// <remarks>
/// Three states must stay distinguishable, because collapsing any two writes the wrong thing back
/// to Google later:
/// <list type="bullet">
/// <item><description><see cref="InheritsCalendarDefault"/> — follows the calendar's defaults.
/// Timed events only: FHQ-193 found an all-day event never inherits, because Google materialises
/// the default into explicit overrides at creation.</description></item>
/// <item><description><see cref="Explicit"/> — the event carries its own list.</description></item>
/// <item><description><see cref="ExplicitlyNone"/> — Google sent <c>useDefault:false</c> and no
/// <c>overrides</c> key. On an all-day event this is ambiguous: it also covers reminders the API
/// refuses to show (FHQ-193 §10), so nothing downstream may describe it as "no reminders".</description></item>
/// </list>
/// A fourth state — not yet synced — is the <c>null</c> reference, not a value here.
/// </remarks>
public sealed class EventReminders
{
    /// <summary>Whether the calendar's default reminders apply to this event.</summary>
    public bool UseDefault { get; init; }

    /// <summary>
    /// The event's own reminders. Never null. Order is NOT meaningful — Google reorders the array
    /// (FHQ-193, fixture 25) — so compare with <see cref="SameAs"/> rather than by sequence.
    /// </summary>
    // NOT `= []`: for an IReadOnlyList target that compiles to Array.Empty<T>(), which is fixed-size.
    // EF's navigation fixup Adds into this collection when materialising a TRACKED query, and throws
    // NotSupportedException on a fixed-size one — taking down every tracked read of an event that has
    // reminders (sync updates, kiosk edit, kiosk delete). A mutable List is required. EF mutates it
    // during fixup, so this type is immutable by convention, not by construction.
    public IReadOnlyList<EventReminder> Overrides { get; init; } = new List<EventReminder>();

    /// <summary>Follows the calendar's defaults, and changes with them.</summary>
    /// <remarks>
    /// A factory PROPERTY, not a <c>static readonly</c> field: EF owned types (JSON-mapped included)
    /// cannot share one CLR instance across two owners, so every access must yield a fresh instance.
    /// A shared field is a trap directly in the path of the next person who assigns it to two events.
    /// </remarks>
    public static EventReminders InheritsCalendarDefault => new() { UseDefault = true };

    /// <summary>Google sent <c>useDefault:false</c> with no overrides. See the remarks on the class.</summary>
    /// <remarks>A factory property for the same reason as <see cref="InheritsCalendarDefault"/>.</remarks>
    public static EventReminders ExplicitlyNone => new() { UseDefault = false };

    /// <summary>The event carries its own reminders. An empty sequence yields the explicitly-none shape.</summary>
    public static EventReminders Explicit(IEnumerable<EventReminder> overrides) =>
        new() { UseDefault = false, Overrides = overrides.ToList() };

    /// <summary>
    /// Value equality that ignores order but counts duplicates — the comparison that matches how
    /// Google actually behaves. Used to decide whether a synced event's reminders really changed.
    /// </summary>
    public bool SameAs(EventReminders? other)
    {
        if (other is null) return false;
        if (UseDefault != other.UseDefault) return false;
        if (Overrides.Count != other.Overrides.Count) return false;

        static IOrderedEnumerable<EventReminder> Ordered(IReadOnlyList<EventReminder> r) =>
            r.OrderBy(x => x.Method, StringComparer.Ordinal).ThenBy(x => x.Minutes);

        return Ordered(Overrides).SequenceEqual(Ordered(other.Overrides));
    }
}

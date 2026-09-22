using System.Buffers;
using System.Text;
using System.Text.Json;
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FamilyHQ.Data.Configurations;

/// <summary>
/// Maps <see cref="EventReminders"/> to a single opaque <c>jsonb</c> column, shared by
/// <see cref="CalendarInfoConfiguration"/> and <see cref="CalendarEventConfiguration"/>.
/// </summary>
/// <remarks>
/// FHQ-205. This replaces <c>OwnsOne(...).ToJson()</c> + <c>OwnsMany(...)</c>, which modelled a
/// VALUE as an entity graph with identity and produced three production defects in one ticket:
/// <list type="number">
/// <item><description>C1 — <c>Overrides = []</c> compiled to a fixed-size <c>Array.Empty</c> that
/// EF's navigation fixup then tried to <c>Add</c> into.</description></item>
/// <item><description>C2 — one <see cref="EventReminders"/> instance could not be owned by two
/// entities, and silently re-parented instead.</description></item>
/// <item><description>C3 — the P1 incident: EF gives every owned-collection element a SHADOW key
/// (<c>__synthesizedOrdinal</c>) that only exists on a tracked entity. <c>GetCalendarsAsync</c>
/// returns calendars <c>AsNoTracking</c> and <c>UpdateCalendarAsync</c> calls
/// <c>Calendars.Update(...)</c> on that detached graph, so <c>SaveChanges</c> threw
/// "The value of shadow key property ... __synthesizedOrdinal is unknown" and all calendar syncing
/// stopped for ~5 hours.</description></item>
/// </list>
/// With a value converter there is no owned entity, no shadow key, no fixup and no owner identity,
/// so all three defect classes become unrepresentable.
/// <para>
/// The stored JSON is unchanged — <c>{"useDefault":false,"overrides":[{"method":"popup",
/// "minutes":30}]}</c>, Google's own lower-camel-case shape — so existing production rows keep
/// deserialising and the migration emits no DDL. The column stays <c>jsonb</c>, so Postgres
/// normalises key order and whitespace; only the content matters.
/// </para>
/// </remarks>
public static class EventRemindersConversion
{
    private const string UseDefaultProperty = "useDefault";
    private const string OverridesProperty = "overrides";
    private const string MethodProperty = "method";
    private const string MinutesProperty = "minutes";

    /// <summary>The <c>jsonb</c> column type both configurations map this property to.</summary>
    /// <remarks>
    /// Named here rather than left to convention: without it EF maps the converted
    /// <see cref="string"/> to <c>text</c>, which would make the FHQ-205 migration rewrite a column
    /// that already holds production data.
    /// </remarks>
    public const string ColumnType = "jsonb";

    /// <summary>Converts the whole reminders object to and from one JSON document.</summary>
    public static readonly ValueConverter<EventReminders?, string?> Converter = new(
        value => value == null ? null : Serialize(value),
        json => json == null ? null : Deserialize(json));

    /// <summary>
    /// Structural comparison for change tracking, snapshotting and equality.
    /// </summary>
    /// <remarks>
    /// Equality delegates to <see cref="EventReminders.SameAs"/>, which ignores ORDER but counts
    /// DUPLICATES — Google reorders <c>overrides</c> (FHQ-193, fixture 25), so an order-sensitive
    /// comparison would report a change on every sync. The hash is computed over the same
    /// order-independent projection, because a hash that disagrees with the equality causes missed
    /// updates (two equal values hashing apart) or spurious ones. The snapshot deep-copies the
    /// list, without which EF could not detect a mutation of <c>Overrides</c>.
    /// </remarks>
    public static readonly ValueComparer<EventReminders?> Comparer = new(
        (left, right) => left == null ? right == null : right != null && left.SameAs(right),
        value => value == null ? 0 : HashOf(value),
        value => value == null ? null : Copy(value));

    internal static string Serialize(EventReminders value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteBoolean(UseDefaultProperty, value.UseDefault);
            writer.WriteStartArray(OverridesProperty);
            foreach (var reminder in value.Overrides)
            {
                writer.WriteStartObject();
                writer.WriteString(MethodProperty, reminder.Method);
                writer.WriteNumber(MinutesProperty, reminder.Minutes);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    internal static EventReminders Deserialize(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var useDefault = root.TryGetProperty(UseDefaultProperty, out var useDefaultElement)
            && useDefaultElement.ValueKind == JsonValueKind.True;

        // A mutable List, never an array: EventReminders documents why a fixed-size collection is
        // unsafe here (C1), and the list is the instance the comparer's snapshot is taken from.
        var overrides = new List<EventReminder>();

        // Google OMITS `overrides` entirely for the explicitly-none shape (useDefault:false and no
        // reminders), so a missing key is a normal document and must read back as an empty list —
        // never null, which EventReminders.Overrides forbids.
        if (root.TryGetProperty(OverridesProperty, out var overridesElement)
            && overridesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in overridesElement.EnumerateArray())
            {
                // Method stays a STRING and Minutes stays SIGNED, both deliberately — see the
                // EventReminder docs. An unknown method or an out-of-range minutes value must
                // round-trip rather than be normalised away.
                var method = element.GetProperty(MethodProperty).GetString()
                    ?? throw new JsonException($"'{MethodProperty}' was null in a stored reminder override.");
                var minutes = element.GetProperty(MinutesProperty).GetInt32();
                overrides.Add(new EventReminder(method, minutes));
            }
        }

        return new EventReminders { UseDefault = useDefault, Overrides = overrides };
    }

    internal static int HashOf(EventReminders value)
    {
        var hash = new HashCode();
        hash.Add(value.UseDefault);
        foreach (var reminder in Ordered(value))
        {
            hash.Add(reminder.Method, StringComparer.Ordinal);
            hash.Add(reminder.Minutes);
        }

        return hash.ToHashCode();
    }

    internal static EventReminders Copy(EventReminders value) => new()
    {
        UseDefault = value.UseDefault,
        // EventReminder is an immutable record, so copying the list is a deep copy of everything
        // that can change.
        Overrides = value.Overrides.ToList()
    };

    private static IOrderedEnumerable<EventReminder> Ordered(EventReminders value) =>
        value.Overrides
            .OrderBy(reminder => reminder.Method, StringComparer.Ordinal)
            .ThenBy(reminder => reminder.Minutes);
}

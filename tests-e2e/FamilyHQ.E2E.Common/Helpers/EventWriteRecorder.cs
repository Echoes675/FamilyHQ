using System.Text.Json;
using Microsoft.Playwright;

namespace FamilyHQ.E2E.Common.Helpers;

/// <summary>
/// Records the kiosk's own writes to the events API — the POST, PUT and DELETE requests the browser
/// sends to <c>/api/events</c> — so a scenario can assert what was actually SENT rather than only
/// what the screen shows afterwards.
/// </summary>
/// <remarks>
/// <para>
/// Reminders need this. A save that sends back the reminders it opened with and a save that says
/// nothing about them leave exactly the same screen behind, so no UI assertion can tell them apart —
/// yet only the second leaves a set made in the Google Calendar app on a phone alone. The request
/// body is the one place the difference is observable from outside the app, and it is the seam the
/// rule lives on: the request either carries a <c>reminders</c> object or it does not.
/// </para>
/// <para>
/// Attached per scenario to that scenario's own page, so nothing leaks between scenarios under the
/// parallel runner.
/// </para>
/// </remarks>
public sealed class EventWriteRecorder
{
    private readonly List<EventWrite> _writes = [];
    private readonly object _gate = new();

    private EventWriteRecorder()
    {
    }

    /// <summary>
    /// Starts recording the event-API writes made by <paramref name="page"/>. Page-level, so the
    /// recording survives the navigations the login flow performs.
    /// </summary>
    public static EventWriteRecorder AttachTo(IPage page)
    {
        var recorder = new EventWriteRecorder();
        page.Request += recorder.OnRequest;
        return recorder;
    }

    /// <summary>Every recorded write, oldest first.</summary>
    public IReadOnlyList<EventWrite> Writes
    {
        get
        {
            lock (_gate)
            {
                return [.. _writes];
            }
        }
    }

    /// <summary>
    /// The most recent write, which is the one the step under assertion caused.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// When the kiosk sent no write at all. Thrown rather than returning null so a scenario that
    /// silently saved nothing fails as loudly as one that saved the wrong thing.
    /// </exception>
    public EventWrite Last
    {
        get
        {
            lock (_gate)
            {
                return _writes.Count > 0
                    ? _writes[^1]
                    : throw new InvalidOperationException(
                        "The kiosk sent no create, update or delete to the events API during this scenario.");
            }
        }
    }

    private void OnRequest(object? sender, IRequest request)
    {
        // The dashboard's own polling GET is api/calendars/events — a different path, and not a
        // write. OPTIONS preflights carry no body and say nothing about intent.
        if (!request.Url.Contains("/api/events", StringComparison.OrdinalIgnoreCase)
            || request.Method is not ("POST" or "PUT" or "DELETE"))
        {
            return;
        }

        lock (_gate)
        {
            _writes.Add(new EventWrite(request.Method, request.Url, request.PostData));
        }
    }
}

/// <summary>One recorded write to the events API, with its body as the browser sent it.</summary>
public sealed record EventWrite(string Method, string Url, string? Body)
{
    /// <summary>
    /// The <c>reminders</c> object the write carried, or null when it said nothing about them.
    /// </summary>
    /// <remarks>
    /// A missing key and an explicit <c>null</c> mean the same thing — the family did not touch the
    /// Reminders tab — so both read as null here. Property names are matched case-insensitively
    /// because the casing is the serializer's business, not the behaviour's.
    /// </remarks>
    public RecordedReminders? Reminders
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Body))
            {
                return null;
            }

            using var document = JsonDocument.Parse(Body);
            if (!TryGetProperty(document.RootElement, "reminders", out var reminders)
                || reminders.ValueKind is JsonValueKind.Null)
            {
                return null;
            }

            var useDefault = TryGetProperty(reminders, "useDefault", out var useDefaultElement)
                && useDefaultElement.ValueKind is JsonValueKind.True;

            var overrides = TryGetProperty(reminders, "overrides", out var overridesElement)
                && overridesElement.ValueKind is JsonValueKind.Array
                ? overridesElement.EnumerateArray().Select(ToOverride).ToList()
                : [];

            return new RecordedReminders(useDefault, overrides);
        }
    }

    private static RecordedReminder ToOverride(JsonElement element) => new(
        TryGetProperty(element, "method", out var method) ? method.GetString() ?? "" : "",
        TryGetProperty(element, "minutes", out var minutes) ? minutes.GetInt32() : 0);

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind is JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}

/// <summary>The reminders a write asked for, in Google's own shape.</summary>
/// <param name="UseDefault">Whether the write asked for the calendar's usual reminders.</param>
/// <param name="Overrides">The reminders the write asked for instead. Compared as a set — Google reorders.</param>
public sealed record RecordedReminders(bool UseDefault, IReadOnlyList<RecordedReminder> Overrides);

/// <summary>One reminder in a recorded write.</summary>
public sealed record RecordedReminder(string Method, int Minutes);

using System.Security.Cryptography;
using System.Text;
using FamilyHQ.Core.Models;

namespace FamilyHQ.Services.Calendar;

/// <summary>
/// Utility for computing stable hashes of event content.
/// Used to detect self-generated echo webhooks from Google Calendar.
/// </summary>
public static class EventContentHash
{
    // ASCII unit separator — cannot appear in user-entered calendar text, so it cannot be used to
    // forge a collision between two different field sets.
    private const char Sep = '\u001F';

    /// <summary>
    /// Computes a stable hex hash of the key event fields, plus the reminders this write is sending
    /// if it is sending any.
    /// </summary>
    /// <param name="reminders">
    /// The reminders this write <b>sends</b>, or null when the write says nothing about reminders.
    /// <para>
    /// Null must be the default and must contribute nothing to the digest. The value is stamped onto
    /// the event and is the key the self-echo guard matches on, and the vast majority of events in
    /// production were stamped by a build that had no notion of reminders at all. Feeding in the
    /// reminders an event merely <i>holds</i> — rather than the ones a write <i>sends</i> — would
    /// change the stamp for every ordinary title or time edit, and the guard would stop recognising
    /// FamilyHQ's own writes: they would come back through the sync as inbound changes.
    /// </para>
    /// </param>
    public static string Compute(
        string title,
        DateTimeOffset start,
        DateTimeOffset end,
        bool isAllDay,
        string? description,
        EventReminders? reminders = null)
    {
        var desc = string.IsNullOrEmpty(description) ? "" : description;
        var input = $"{title}{Sep}{start.ToUniversalTime():O}{Sep}{end.ToUniversalTime():O}{Sep}{isAllDay}{Sep}{desc}";

        // Appended only for a write that carries reminders, so a write that does not carry them keeps
        // the exact digest earlier builds produced for the same event. See the parameter's remarks.
        if (reminders is not null)
            input += $"{Sep}{Canonicalise(reminders)}";

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// A reminder set rendered order-independently. Google does not preserve the order of the
    /// overrides array — send 10 then 60 and it returns 60 then 10 — so an order-sensitive rendering
    /// would make its echo of our own write look like somebody else's change. Duplicates are kept,
    /// because the digest describes what was sent and Google's de-duplication happens after that.
    /// </summary>
    private static string Canonicalise(EventReminders reminders)
    {
        var overrides = reminders.Overrides
            .OrderBy(o => o.Method, StringComparer.Ordinal)
            .ThenBy(o => o.Minutes)
            .Select(o => $"{o.Method}:{o.Minutes}");

        return $"{reminders.UseDefault}{Sep}{string.Join(',', overrides)}";
    }
}

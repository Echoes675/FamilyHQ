namespace FamilyHQ.Core.DTOs;

/// <summary>
/// The whole reminders-timeline response: the household's own "today", the instant that answer stops
/// being true, and the rows.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the anchor travels with the rows.</b> The kiosk used to read "today" off its own clock while
/// the server bounded this window with the zone FamilyHQ has STORED. Those agree whenever the stored
/// zone was auto-detected — the kiosk's reported OS zone is what gets stored — and are simply two
/// different zones once the family has chosen one explicitly, or one was derived from a saved
/// location, or the stored id will not resolve. Two implementations of one rule with the same inputs
/// is the arrangement that produced this response shape: they agreed in the common case and diverged
/// at a boundary, where rows for the household's today fell outside the kiosk's and were dropped
/// silently.
/// </para>
/// <para>
/// <b>Why the rollover instant is a field and not a client calculation.</b> A local day is not always
/// 24 hours long. Expressed as an instant it needs no zone on the client at all, and the one place
/// that knows the zone's offsets is the one computing it.
/// </para>
/// </remarks>
/// <param name="Today">
/// The local date the window opens on, in the household zone. The client's anchor for which panel a
/// row goes in, whether it reads "Today" or "Tomorrow", and the count on the Reminders tab.
/// </param>
/// <param name="NextRolloverUtc">
/// The next local midnight, as an instant — 23 or 25 hours away on a daylight-saving transition day
/// rather than always 24. When the kiosk's clock passes it, <see cref="Today"/> is stale and the rows
/// are refetched. At a zero offset, as the name says: the household's own offset is deliberately not
/// carried here, so that no reader can take a local date off this value and arrive at a different
/// day from <see cref="Today"/>. It answers "when", and only "when".
/// </param>
/// <param name="Rows">The timeline's rows, in the order the view renders them.</param>
public sealed record UpcomingRemindersDto(
    DateOnly Today,
    DateTimeOffset NextRolloverUtc,
    IReadOnlyList<UpcomingReminderEventDto> Rows);

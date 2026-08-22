namespace FamilyHQ.Core.Models;

/// <summary>
/// Lightweight result of IGoogleCalendarClient.GetEventAsync.
/// Used by the webhook handler to detect self-generated echo events.
/// <para>
/// FHQ-166: this deliberately carries no <c>OrganizerEmail</c>. It used to, populated from the real
/// organiser address on every fetch, but nothing in <c>src/</c> ever read it — only a mapping test
/// asserted on it. That is a live email address held in memory purely to be asserted on, one
/// <c>{@Detail}</c> or one serialiser away from a log sink. Echo detection uses
/// <see cref="ContentHash"/>.
/// </para>
/// </summary>
/// <param name="Id">The Google event id.</param>
/// <param name="ContentHash">The <c>extendedProperties.private["content-hash"]</c> echo marker.</param>
/// <param name="IanaTimeZone">
/// FHQ-164: Google's <c>start.timeZone</c> for this event. A recurring INSTANCE carries the zone its
/// series is anchored to, which makes any surviving instance a Google-supplied answer for the
/// series' zone when the master itself cannot be fetched. Null when Google supplied none (an all-day
/// event, or a single timed event with no explicit zone).
/// </param>
public record GoogleEventDetail(
    string Id,
    string? ContentHash,
    string? IanaTimeZone = null);

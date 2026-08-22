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
public record GoogleEventDetail(
    string Id,
    string? ContentHash);

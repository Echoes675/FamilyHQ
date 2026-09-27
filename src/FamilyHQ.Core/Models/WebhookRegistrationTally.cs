namespace FamilyHQ.Core.Models;

/// <summary>
/// FHQ-213. What one webhook-registration pass actually did, so the renewal loop can emit a single
/// Information summary per pass instead of one line per calendar per hour.
/// <para>
/// This exists because the counts and the reader are in different places: only
/// <c>WebhookRegistrationService</c> knows the per-calendar outcome, and only
/// <c>WebhookRenewalService</c> knows it is a scheduled pass and when the next one is. Returning the
/// counts as data keeps both honest — no logging decision is pushed down into the registration path,
/// and no per-calendar state is plumbed up through anything that should not care.
/// </para>
/// <para>
/// A struct on purpose: an unstubbed mock then yields <c>(0, 0, 0)</c> rather than null, so no caller
/// can be tripped up by a tally that was never produced.
/// </para>
/// </summary>
/// <param name="CalendarsChecked">
/// Calendars whose channel was examined. Excludes calendars skipped before the check — ones marked as
/// not supporting push, and every calendar when registration is disabled or no address is configured.
/// </param>
/// <param name="ChannelsRegistered">
/// Channels re-registered with Google: due inside <c>RenewalWindow</c>, registered for a stale
/// address (FHQ-196), forced, already lapsed, or brand new. This is the count that costs a Google call.
/// </param>
/// <param name="ChannelsFoundExpired">
/// Channels already past their expiry when the pass found them — push had stopped before we looked.
/// Each is also counted in <paramref name="ChannelsRegistered"/> when re-registration succeeded.
/// </param>
public readonly record struct WebhookRegistrationTally(
    int CalendarsChecked,
    int ChannelsRegistered,
    int ChannelsFoundExpired)
{
    /// <summary>Nothing examined — the identity for summing.</summary>
    public static WebhookRegistrationTally None => default;

    public static WebhookRegistrationTally operator +(WebhookRegistrationTally left, WebhookRegistrationTally right) =>
        new(left.CalendarsChecked + right.CalendarsChecked,
            left.ChannelsRegistered + right.ChannelsRegistered,
            left.ChannelsFoundExpired + right.ChannelsFoundExpired);
}

namespace FamilyHQ.Core.DTOs;

/// <summary>
/// One Google push channel registration, as the diagnostics endpoint reports it (FHQ-141).
/// <para>
/// A smoke run has to be able to answer "is this environment still wired to receive Google push?"
/// before it asserts that a change made on a phone reaches the kiosk — otherwise a silent channel
/// expiry reads as a FamilyHQ bug. Google offers no way to list channels, so FamilyHQ's own
/// registrations are the only place to ask.
/// </para>
/// <para>
/// Deliberately minimal. The channel id and <c>ChannelToken</c> are absent: the token is the credential
/// that authorises posting a notification to FamilyHQ, and nothing about "has this calendar got a live
/// channel?" needs either. The calendar is named by FamilyHQ's own id rather than the Google calendar id
/// or the display name, both of which are the account's or a family member's identity (FHQ-166); a
/// caller that needs the name joins against <c>GET /api/calendars</c>.
/// </para>
/// </summary>
public record WebhookRegistrationStatusDto(
    Guid CalendarInfoId,
    DateTimeOffset ExpiresAt,
    DateTimeOffset RegisteredAt);

namespace FamilyHQ.Smoke.Data.Models;

/// <summary>
/// One of preprod's Google push channel registrations, as
/// <c>GET /api/diagnostics/webhook-registrations</c> describes it (added for FHQ-141).
/// <para>
/// Identified by FamilyHQ's own calendar id, never by the Google calendar id and never by the channel
/// token: the first is an email address and the second is the credential that authorises posting a
/// notification to FamilyHQ. The smoke suite joins this against <c>GET /api/calendars</c> to learn
/// which calendar each registration belongs to.
/// </para>
/// </summary>
public sealed record PreprodWebhookRegistration(
    Guid CalendarInfoId, DateTimeOffset ExpiresAt, DateTimeOffset RegisteredAt);

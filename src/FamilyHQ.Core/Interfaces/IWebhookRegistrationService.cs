namespace FamilyHQ.Core.Interfaces;

using FamilyHQ.Core.Models;

public interface IWebhookRegistrationService
{
    Task RegisterForCalendarAsync(Guid calendarInfoId, string googleCalendarId, bool force = false, CancellationToken ct = default);

    /// <summary>
    /// FHQ-213: returns what it did, so a caller running this as a scheduled pass can report the
    /// outcome in one line. Foreground callers (login, a forced re-register) can ignore it.
    /// </summary>
    Task<WebhookRegistrationTally> RegisterAllAsync(string userId, bool force = false, CancellationToken ct = default);

    /// <inheritdoc cref="RegisterAllAsync"/>
    Task<WebhookRegistrationTally> RenewAllAsync(CancellationToken ct = default);
}

namespace FamilyHQ.Core.Interfaces;

public interface IWebhookRegistrationService
{
    Task RegisterForCalendarAsync(Guid calendarInfoId, string googleCalendarId, bool force = false, CancellationToken ct = default);
    Task RegisterAllAsync(string userId, bool force = false, CancellationToken ct = default);
    Task RenewAllAsync(CancellationToken ct = default);
}

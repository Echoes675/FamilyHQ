namespace FamilyHQ.Core.Interfaces;

public interface IWebhookRegistrationService
{
    Task RegisterForCalendarAsync(Guid calendarInfoId, string googleCalendarId, CancellationToken ct = default);
    Task RegisterAllAsync(string userId, CancellationToken ct = default);
    Task RenewAllAsync(CancellationToken ct = default);
}

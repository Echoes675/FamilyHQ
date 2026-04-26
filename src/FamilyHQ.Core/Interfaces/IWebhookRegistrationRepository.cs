using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface IWebhookRegistrationRepository
{
    Task<WebhookRegistration?> GetByChannelIdAsync(string channelId, CancellationToken ct = default);
    Task<IReadOnlyList<WebhookRegistration>> GetAllAsync(CancellationToken ct = default);
    Task UpsertAsync(WebhookRegistration registration, CancellationToken ct = default);
    Task DeleteByCalendarIdAsync(Guid calendarInfoId, CancellationToken ct = default);
}

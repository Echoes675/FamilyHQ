using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Data.Repositories;

public class WebhookRegistrationRepository(FamilyHqDbContext context) : IWebhookRegistrationRepository
{
    public async Task<WebhookRegistration?> GetByChannelIdAsync(string channelId, CancellationToken ct = default)
        => await context.WebhookRegistrations
            .FirstOrDefaultAsync(w => w.ChannelId == channelId, ct);

    public async Task<IReadOnlyList<WebhookRegistration>> GetAllAsync(CancellationToken ct = default)
        => await context.WebhookRegistrations
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task UpsertAsync(WebhookRegistration registration, CancellationToken ct = default)
    {
        var existing = await context.WebhookRegistrations
            .FirstOrDefaultAsync(w => w.CalendarInfoId == registration.CalendarInfoId, ct);

        if (existing is null)
        {
            context.WebhookRegistrations.Add(registration);
        }
        else
        {
            existing.ChannelId = registration.ChannelId;
            existing.ResourceId = registration.ResourceId;
            existing.ExpiresAt = registration.ExpiresAt;
            existing.RegisteredAt = registration.RegisteredAt;
        }

        await context.SaveChangesAsync(ct);
    }

    public async Task DeleteByCalendarIdAsync(Guid calendarInfoId, CancellationToken ct = default)
    {
        var existing = await context.WebhookRegistrations
            .FirstOrDefaultAsync(w => w.CalendarInfoId == calendarInfoId, ct);

        if (existing is not null)
        {
            context.WebhookRegistrations.Remove(existing);
            await context.SaveChangesAsync(ct);
        }
    }
}

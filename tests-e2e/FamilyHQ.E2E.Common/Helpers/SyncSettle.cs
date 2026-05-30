namespace FamilyHQ.E2E.Common.Helpers;

using Microsoft.Playwright;

/// <summary>
/// Deterministic "sync settled" barrier. The webhook (FHQ-37) and app-edit echoes enqueue a
/// durable CalendarSyncJob and return; the single-consumer worker drains it a beat later and
/// broadcasts EventsUpdated. Without awaiting the drain, an immediately-following assertion
/// races the worker (intermittent-issues #6). Polls the per-user
/// /api/diagnostics/sync-queue-depth (Bearer token from localStorage) until Pending+InProgress
/// reaches 0. Degrades gracefully: no auth context -> brief settle + return; never drains within
/// the deadline -> return (downstream assertions carry their own polling). The ONLY delay is the
/// poll interval — never a fixed settle timeout. Idempotent + cheap when idle (active==0 returns
/// immediately), so it is safe to call after any act-step regardless of whether it synced.
/// </summary>
public static class SyncSettle
{
    public static async Task WaitForUserQueueDrainAsync(IPage page, int deadlineSeconds = 40)
    {
        var deadline = DateTime.UtcNow.AddSeconds(deadlineSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var active = await page.EvaluateAsync<int>(@"
                async () => {
                    try {
                        const t = localStorage.getItem('familyhq_auth_token');
                        if (!t) return -2; // no auth context on this page yet
                        const r = await fetch('/api/diagnostics/sync-queue-depth', { headers: { 'Authorization': 'Bearer ' + t } });
                        if (r.status === 401) return -2;
                        if (!r.ok) return -1; // transient — keep polling
                        const b = await r.json();
                        return (b && typeof b.active === 'number') ? b.active : -1;
                    } catch (e) { return -1; }
                }");

            if (active == 0)
                return; // this user's queue has drained — sync applied

            if (active == -2)
            {
                // Not authenticated on this page — the barrier doesn't apply; brief settle then proceed.
                await Task.Delay(1500);
                return;
            }

            await Task.Delay(500);
        }
        // Deadline elapsed: proceed; downstream assertions have their own waits/polls.
    }
}

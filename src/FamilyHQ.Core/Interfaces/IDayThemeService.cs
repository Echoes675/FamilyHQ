using FamilyHQ.Core.DTOs;

namespace FamilyHQ.Core.Interfaces;

public interface IDayThemeService
{
    /// <summary>
    /// Creates today's row for one kiosk if it is missing. A kiosk with no saved
    /// <c>LocationSetting</c> gets no row (FHQ-177) — there is nothing to compute boundaries from,
    /// and guessing via server-side IP geolocation returns the hosting datacentre.
    /// </summary>
    Task EnsureTodayAsync(string userId, CancellationToken ct = default);

    Task RecalculateForTodayAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Today's boundaries and current period for one kiosk, or <c>null</c> when that kiosk has no
    /// saved location. Null is a normal state, not a fault: the client falls back to its default
    /// theme.
    /// </summary>
    Task<DayThemeDto?> GetTodayAsync(string userId, CancellationToken ct = default);
}

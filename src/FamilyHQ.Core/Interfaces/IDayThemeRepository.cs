using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface IDayThemeRepository
{
    /// <summary>
    /// FHQ-177: scoped to one kiosk. Rows are unique per (UserId, Date), so two kiosks in different
    /// places each hold their own boundaries for the same calendar date.
    /// </summary>
    Task<DayTheme?> GetByDateAsync(string userId, DateOnly date, CancellationToken ct = default);

    Task<DayTheme> UpsertAsync(DayTheme dayTheme, CancellationToken ct = default);
}

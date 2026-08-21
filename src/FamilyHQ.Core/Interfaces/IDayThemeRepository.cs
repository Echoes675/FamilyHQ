using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface IDayThemeRepository
{
    Task<DayTheme?> GetByDateAsync(DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// The stored row with the greatest <see cref="DayTheme.Date"/>, or null when the table is empty.
    /// FHQ-134: its <see cref="DayTheme.IanaTimeZone"/> is the family's effective zone, and is what the
    /// theme service converts "now" into to derive the local date key. Read-only, no tracking.
    /// </summary>
    Task<DayTheme?> GetMostRecentAsync(CancellationToken ct = default);
    Task<DayTheme> UpsertAsync(DayTheme dayTheme, CancellationToken ct = default);
}

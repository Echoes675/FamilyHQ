using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface IDayThemeRepository
{
    Task<DayTheme?> GetByDateAsync(DateOnly date, CancellationToken ct = default);
    Task<DayTheme> UpsertAsync(DayTheme dayTheme, CancellationToken ct = default);
}

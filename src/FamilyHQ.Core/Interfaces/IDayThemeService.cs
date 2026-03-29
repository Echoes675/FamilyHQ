using FamilyHQ.Core.DTOs;

namespace FamilyHQ.Core.Interfaces;

public interface IDayThemeService
{
    Task EnsureTodayAsync(CancellationToken ct = default);
    Task RecalculateForTodayAsync(CancellationToken ct = default);
    Task<DayThemeDto> GetTodayAsync(CancellationToken ct = default);
}

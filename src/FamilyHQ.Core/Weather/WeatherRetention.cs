namespace FamilyHQ.Core.Weather;

using System.Linq.Expressions;
using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Models;

/// <summary>
/// FHQ-159: the rule for which stored weather rows a refresh is allowed to replace.
/// <para>
/// This is domain logic, not a data-access detail, so it lives in Core alongside
/// <see cref="WeatherDataType"/> rather than being exposed off the repository that happens to
/// execute it. The repository composes these two into one set-based delete; nothing else may
/// widen the predicate.
/// </para>
/// </summary>
public static class WeatherRetention
{
    /// <summary>
    /// The sections a refresh replaces: exactly those its payload carried rows for. A section whose
    /// Open-Meteo block came back empty contributes no rows, so it is not replaced — and a payload
    /// carrying nothing at all replaces nothing.
    /// </summary>
    public static List<WeatherDataType> SectionsReplacedBy(List<WeatherDataPoint> dataPoints) =>
        [.. dataPoints.Select(x => x.DataType).Distinct()];

    /// <summary>
    /// The stored rows that a refresh carrying <paramref name="sections"/> replaces: this location's
    /// rows in those sections and no others.
    /// <para>
    /// The predicate used to match on <c>LocationSettingId</c> alone, so a payload whose
    /// <c>hourly</c> block came back empty wiped the stored hourly rows as a side effect of
    /// rewriting <c>daily</c> — one degraded Open-Meteo response blanked forecast the kiosk was
    /// showing correctly. Narrowing it to the sections actually carried is the whole fix; it stays a
    /// single set-based statement (<c>data_type = ANY(...)</c>), preserving the FHQ-52 shape.
    /// </para>
    /// </summary>
    public static Expression<Func<WeatherDataPoint, bool>> RowsReplacedBy(
        int locationSettingId, List<WeatherDataType> sections) =>
        x => x.LocationSettingId == locationSettingId && sections.Contains(x.DataType);
}

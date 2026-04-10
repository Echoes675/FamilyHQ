namespace FamilyHQ.Core.Enums;

/// <summary>
/// Describes what happened when <see cref="FamilyHQ.Core.Interfaces.IWeatherRefreshService"/>
/// was invoked for a given user.  Exists so callers can react to silent skips
/// (which previously caused intermittent E2E flakes — a "refresh" that stored
/// nothing returned 200 and the downstream /current call then returned 204).
/// </summary>
public enum WeatherRefreshOutcome
{
    /// <summary>Refresh ran, weather data was fetched from the provider and persisted.</summary>
    Succeeded,

    /// <summary>Refresh was skipped because the user has weather disabled.</summary>
    SkippedWeatherDisabled,

    /// <summary>Refresh was skipped because the user has no saved location yet.</summary>
    SkippedNoLocation
}

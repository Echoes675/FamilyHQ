namespace FamilyHQ.Core.DTOs;

using FamilyHQ.Core.Enums;

/// <summary>
/// Structured outcome of a <see cref="FamilyHQ.Core.Interfaces.IWeatherRefreshService"/>
/// refresh call.  Callers use this to distinguish a genuine success from a
/// silent skip (the previous void return swallowed skip reasons and caused
/// intermittent E2E flakes where /api/weather/refresh returned 200 despite
/// storing zero data points).
/// </summary>
/// <param name="Outcome">What happened on this invocation.</param>
/// <param name="LocationSettingId">The resolved location id when <see cref="Outcome"/> is <see cref="WeatherRefreshOutcome.Succeeded"/>; null otherwise.</param>
/// <param name="DataPointsWritten">Number of weather data points persisted when <see cref="Outcome"/> is <see cref="WeatherRefreshOutcome.Succeeded"/>; 0 otherwise.</param>
public record WeatherRefreshResult(
    WeatherRefreshOutcome Outcome,
    int? LocationSettingId,
    int DataPointsWritten);

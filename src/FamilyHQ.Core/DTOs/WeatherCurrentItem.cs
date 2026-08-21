namespace FamilyHQ.Core.DTOs;

using FamilyHQ.Core.Enums;

/// <summary>
/// The "conditions right now" reading. FHQ-159: a null <see cref="WeatherResponse.Current"/> means
/// the provider returned no current block at all, which is deliberately distinguishable from a
/// reading whose condition merely parsed as <see cref="WeatherCondition.Unknown"/>. Nothing
/// downstream may substitute a zero temperature or wind speed for the absent case — a current
/// reading asserts something about now, so an invented one is wrong rather than incomplete.
/// </summary>
public record WeatherCurrentItem(
    WeatherCondition Condition,
    double TemperatureCelsius,
    double WindSpeedKmh);

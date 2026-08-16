namespace FamilyHQ.Services.Weather;

using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Interfaces;

// Pure lookup with no dependencies of its own. FHQ-115: it reports an unmapped code back
// to the caller rather than logging, so a 16-day forecast full of one unrecognised code
// produces a single aggregated warning instead of hundreds.
public class WmoCodeMapper : IWmoCodeMapper
{
    public bool TryGetCondition(int wmoCode, out WeatherCondition condition)
    {
        condition = wmoCode switch
        {
            0 => WeatherCondition.Clear,
            1 or 2 => WeatherCondition.PartlyCloudy,
            3 => WeatherCondition.Cloudy,
            45 or 48 => WeatherCondition.Fog,
            51 or 53 or 55 => WeatherCondition.Drizzle,
            56 or 57 => WeatherCondition.Sleet,
            61 or 80 => WeatherCondition.LightRain,
            63 or 65 or 81 or 82 => WeatherCondition.HeavyRain,
            66 or 67 => WeatherCondition.Sleet,
            71 or 73 or 75 or 77 or 85 or 86 => WeatherCondition.Snow,
            95 or 96 or 99 => WeatherCondition.Thunder,
            _ => WeatherCondition.Unknown
        };

        return condition is not WeatherCondition.Unknown;
    }
}

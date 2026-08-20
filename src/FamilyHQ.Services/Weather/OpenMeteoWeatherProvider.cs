namespace FamilyHQ.Services.Weather;

using System.Globalization;
using System.Net.Http.Json;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Interfaces;
using Microsoft.Extensions.Logging;
using NodaTime;
using NodaTime.Text;

public class OpenMeteoWeatherProvider(
    HttpClient httpClient,
    IWmoCodeMapper wmoCodeMapper,
    ILogger<OpenMeteoWeatherProvider> logger) : IWeatherProvider
{
    private const string HourlySection = "hourly";
    private const string DailySection = "daily";

    // Open-Meteo returns minute-precision timestamps: "2026-06-18T14:00"
    private static readonly LocalDateTimePattern OpenMeteoLocalDateTimePattern =
        LocalDateTimePattern.CreateWithInvariantCulture("uuuu-MM-dd'T'HH:mm");

    public async Task<WeatherResponse> GetWeatherAsync(double latitude, double longitude,
        string? ianaTimeZone, CancellationToken ct = default)
    {
        var lat = latitude.ToString(CultureInfo.InvariantCulture);
        var lon = longitude.ToString(CultureInfo.InvariantCulture);

        var url = $"v1/forecast?latitude={lat}&longitude={lon}"
            + "&current=temperature_2m,weather_code,wind_speed_10m"
            + "&hourly=temperature_2m,weather_code,wind_speed_10m"
            + "&daily=weather_code,temperature_2m_max,temperature_2m_min,wind_speed_10m_max"
            + "&forecast_days=16"
            + "&timezone=auto";

        var apiResponse = await httpClient.GetFromJsonAsync<OpenMeteoApiResponse>(url, ct)
            ?? throw new InvalidOperationException("Weather API returned null response.");

        var zone = ianaTimeZone is not null
            ? DateTimeZoneProviders.Tzdb.GetZoneOrNull(ianaTimeZone)
            : null;

        var unmappedCodes = new SortedSet<int>();

        // FHQ-115 stopped an absent current block being reported as "clear, 0 °C, 0 km/h".
        // FHQ-159 finishes the job: report the absence itself rather than an Unknown condition
        // carrying invented zeroes, so the refresh can decline to write a current reading at all.
        WeatherCurrentItem? current = null;

        if (apiResponse.Current is not null)
        {
            current = new WeatherCurrentItem(
                MapCondition(apiResponse.Current.WeatherCode, unmappedCodes),
                apiResponse.Current.Temperature,
                apiResponse.Current.WindSpeed);
        }
        else
        {
            logger.LogWarning(
                "Open-Meteo returned no current block; no current reading will be stored and the previously stored one stands until it ages out.");
        }

        var hourly = new List<WeatherHourlyItem>();
        if (apiResponse.Hourly is not null)
        {
            var block = apiResponse.Hourly;
            var count = ResolveUsableCount(HourlySection,
                ("time", CountOf(block.Time)),
                ("temperature_2m", CountOf(block.Temperature)),
                ("weather_code", CountOf(block.WeatherCode)),
                ("wind_speed_10m", CountOf(block.WindSpeed)));

            for (var i = 0; i < count; i++)
            {
                var temp = block.Temperature[i];
                var code = block.WeatherCode[i];
                var wind = block.WindSpeed[i];
                if (temp is null || code is null || wind is null) continue;
                hourly.Add(new WeatherHourlyItem(
                    ToLocalDateTimeOffset(block.Time[i], zone),
                    MapCondition(code.Value, unmappedCodes),
                    temp.Value,
                    wind.Value));
            }
        }

        var daily = new List<WeatherDailyItem>();
        if (apiResponse.Daily is not null)
        {
            var block = apiResponse.Daily;
            var count = ResolveUsableCount(DailySection,
                ("time", CountOf(block.Time)),
                ("weather_code", CountOf(block.WeatherCode)),
                ("temperature_2m_max", CountOf(block.TemperatureMax)),
                ("temperature_2m_min", CountOf(block.TemperatureMin)),
                ("wind_speed_10m_max", CountOf(block.WindSpeedMax)));

            for (var i = 0; i < count; i++)
            {
                var code = block.WeatherCode[i];
                var max = block.TemperatureMax[i];
                var min = block.TemperatureMin[i];
                var wind = block.WindSpeedMax[i];
                if (code is null || max is null || min is null || wind is null) continue;
                daily.Add(new WeatherDailyItem(
                    DateOnly.Parse(block.Time[i], CultureInfo.InvariantCulture),
                    MapCondition(code.Value, unmappedCodes),
                    max.Value,
                    min.Value,
                    wind.Value));
            }
        }

        if (unmappedCodes.Count > 0)
        {
            logger.LogWarning(
                "Open-Meteo returned {UnmappedCodeCount} WMO weather code(s) with no mapping: {UnmappedWmoCodes}. Those entries are reported as the Unknown condition — add them to WmoCodeMapper.",
                unmappedCodes.Count, unmappedCodes.ToArray());
        }

        return new WeatherResponse(current, hourly, daily);
    }

    // FHQ-110: Open-Meteo does not guarantee that the parallel value arrays are the same
    // length as `time` — a truncated or omitted array used to throw while indexing off
    // `time.Count`, which lost the whole refresh. Only the overlap is safe to read.
    private int ResolveUsableCount(string section, params (string Name, int Count)[] arrays)
    {
        var usable = arrays.Min(a => a.Count);

        if (arrays.Any(a => a.Count != usable))
        {
            logger.LogWarning(
                "Open-Meteo returned a ragged {Section} block; parsing the first {UsableCount} entries only. Array lengths: {ArrayLengths}.",
                section, usable, arrays.ToDictionary(a => a.Name, a => a.Count));
        }
        else if (usable == 0)
        {
            // Expected and handled: the Simulator serves empty hourly/daily arrays whenever
            // no forecast rows are seeded, so dev/staging would emit this on every poll.
            logger.LogDebug(
                "Open-Meteo returned an empty {Section} block; no forecast entries will be stored for that section.",
                section);
        }

        return usable;
    }

    private static int CountOf<T>(List<T>? values) => values?.Count ?? 0;

    // FHQ-115: collect the distinct unmapped codes for a single warning per parse instead
    // of one per forecast entry — an unmapped code typically repeats across the whole
    // 16-day forecast.
    private WeatherCondition MapCondition(int wmoCode, SortedSet<int> unmappedCodes)
    {
        if (!wmoCodeMapper.TryGetCondition(wmoCode, out var condition))
            unmappedCodes.Add(wmoCode);

        return condition;
    }

    private static DateTimeOffset ToLocalDateTimeOffset(string s, DateTimeZone? zone)
    {
        if (zone is not null)
        {
            var parseResult = OpenMeteoLocalDateTimePattern.Parse(s);
            if (!parseResult.Success)
                throw new InvalidOperationException(
                    $"Open-Meteo returned an unparseable timestamp '{s}': {parseResult.Exception?.Message}");
            var local = parseResult.Value;
            // AtLeniently: spring-forward gaps and fall-back ambiguity handled gracefully
            // for a weather display context.
            return zone.AtLeniently(local).ToDateTimeOffset();
        }
        return DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }
}

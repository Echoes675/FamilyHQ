using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using NodaTime;
using SunCalcNet;
using SunCalcNet.Model;

namespace FamilyHQ.Services.Theme;

public class SunCalculatorService : ISunCalculatorService
{
    public Task<DayThemeBoundaries> CalculateBoundariesAsync(
        double latitude, double longitude, DateOnly date, string? ianaTimeZone)
    {
        var utcDate = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var sunTimes = SunCalc.GetSunPhases(utcDate, latitude, longitude);

        // FHQ-166: these messages carry the phase name and the date only — never the coordinates
        // they were computed from. Those are the family's home LocationSetting values, passed down
        // by DayThemeService, and an exception message is a log sink: DayThemeSchedulerService logs
        // this at Error, so the home address would land in Seq on every polar-edge failure. The
        // caller supplied the coordinates and already knows them; the phase and the date are the
        // whole of the diagnostic content.
        var dawnPhase = sunTimes.Cast<SunPhase?>().FirstOrDefault(x => x!.Value.Name.Value == SunPhaseName.Dawn.Value)
            ?? throw new InvalidOperationException(
                $"Sun phase 'Dawn' is not available for the configured location on {date}. " +
                "This may occur for polar locations where the sun does not rise or set.");

        var sunrisePhase = sunTimes.Cast<SunPhase?>().FirstOrDefault(x => x!.Value.Name.Value == SunPhaseName.Sunrise.Value)
            ?? throw new InvalidOperationException(
                $"Sun phase 'Sunrise' is not available for the configured location on {date}. " +
                "This may occur for polar locations where the sun does not rise or set.");

        var sunsetPhase = sunTimes.Cast<SunPhase?>().FirstOrDefault(x => x!.Value.Name.Value == SunPhaseName.Sunset.Value)
            ?? throw new InvalidOperationException(
                $"Sun phase 'Sunset' is not available for the configured location on {date}. " +
                "This may occur for polar locations where the sun does not rise or set.");

        var duskPhase = sunTimes.Cast<SunPhase?>().FirstOrDefault(x => x!.Value.Name.Value == SunPhaseName.Dusk.Value)
            ?? throw new InvalidOperationException(
                $"Sun phase 'Dusk' is not available for the configured location on {date}. " +
                "This may occur for polar locations where the sun does not rise or set.");

        var civilDawn = ToLocalTimeOnly(dawnPhase.PhaseTime, ianaTimeZone);
        var sunrise = ToLocalTimeOnly(sunrisePhase.PhaseTime, ianaTimeZone);
        var sunset = ToLocalTimeOnly(sunsetPhase.PhaseTime, ianaTimeZone);
        var civilDusk = ToLocalTimeOnly(duskPhase.PhaseTime, ianaTimeZone);

        var eveningStart = sunset.AddHours(-1);

        return Task.FromResult(new DayThemeBoundaries(civilDawn, sunrise, eveningStart, civilDusk));
    }

    private static TimeOnly ToLocalTimeOnly(DateTime utcPhaseTime, string? ianaTimeZone)
    {
        if (!string.IsNullOrWhiteSpace(ianaTimeZone))
        {
            var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(ianaTimeZone);
            if (zone is not null)
            {
                var utc = DateTime.SpecifyKind(utcPhaseTime, DateTimeKind.Utc);
                var local = Instant.FromDateTimeUtc(utc).InZone(zone).LocalDateTime;
                return new TimeOnly(local.Hour, local.Minute, local.Second);
            }
        }
        return TimeOnly.FromDateTime(utcPhaseTime);
    }
}

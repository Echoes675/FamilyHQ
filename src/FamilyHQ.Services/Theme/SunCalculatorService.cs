using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using SunCalcNet;
using SunCalcNet.Model;

namespace FamilyHQ.Services.Theme;

public class SunCalculatorService : ISunCalculatorService
{
    public Task<DayThemeBoundaries> CalculateBoundariesAsync(double latitude, double longitude, DateOnly date)
    {
        var utcDate = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var sunTimes = SunCalc.GetSunPhases(utcDate, latitude, longitude);

        var dawnPhase = sunTimes.Cast<SunPhase?>().FirstOrDefault(x => x!.Value.Name.Value == SunPhaseName.Dawn.Value)
            ?? throw new InvalidOperationException(
                $"Sun phase 'Dawn' is not available for lat={latitude}, lon={longitude}, date={date}. " +
                "This may occur for polar locations where the sun does not rise or set.");

        var sunrisePhase = sunTimes.Cast<SunPhase?>().FirstOrDefault(x => x!.Value.Name.Value == SunPhaseName.Sunrise.Value)
            ?? throw new InvalidOperationException(
                $"Sun phase 'Sunrise' is not available for lat={latitude}, lon={longitude}, date={date}. " +
                "This may occur for polar locations where the sun does not rise or set.");

        var sunsetPhase = sunTimes.Cast<SunPhase?>().FirstOrDefault(x => x!.Value.Name.Value == SunPhaseName.Sunset.Value)
            ?? throw new InvalidOperationException(
                $"Sun phase 'Sunset' is not available for lat={latitude}, lon={longitude}, date={date}. " +
                "This may occur for polar locations where the sun does not rise or set.");

        var duskPhase = sunTimes.Cast<SunPhase?>().FirstOrDefault(x => x!.Value.Name.Value == SunPhaseName.Dusk.Value)
            ?? throw new InvalidOperationException(
                $"Sun phase 'Dusk' is not available for lat={latitude}, lon={longitude}, date={date}. " +
                "This may occur for polar locations where the sun does not rise or set.");

        var civilDawn = TimeOnly.FromDateTime(dawnPhase.PhaseTime);
        var sunrise = TimeOnly.FromDateTime(sunrisePhase.PhaseTime);
        var sunset = TimeOnly.FromDateTime(sunsetPhase.PhaseTime);
        var civilDusk = TimeOnly.FromDateTime(duskPhase.PhaseTime);

        var eveningStart = sunset.AddHours(-1);

        return Task.FromResult(new DayThemeBoundaries(civilDawn, sunrise, eveningStart, civilDusk));
    }
}

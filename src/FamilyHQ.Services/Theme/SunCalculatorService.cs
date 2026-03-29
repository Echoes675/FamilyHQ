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

        var civilDawn = TimeOnly.FromDateTime(sunTimes.First(x => x.Name.Value == SunPhaseName.Dawn.Value).PhaseTime);
        var sunrise   = TimeOnly.FromDateTime(sunTimes.First(x => x.Name.Value == SunPhaseName.Sunrise.Value).PhaseTime);
        var sunset    = TimeOnly.FromDateTime(sunTimes.First(x => x.Name.Value == SunPhaseName.Sunset.Value).PhaseTime);
        var civilDusk = TimeOnly.FromDateTime(sunTimes.First(x => x.Name.Value == SunPhaseName.Dusk.Value).PhaseTime);

        var eveningStart = sunset.Add(TimeSpan.FromHours(-1));

        return Task.FromResult(new DayThemeBoundaries(civilDawn, sunrise, eveningStart, civilDusk));
    }
}

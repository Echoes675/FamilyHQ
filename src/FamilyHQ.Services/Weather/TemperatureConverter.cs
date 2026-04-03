namespace FamilyHQ.Services.Weather;

using FamilyHQ.Core.Enums;

public static class TemperatureConverter
{
    public static double Convert(double celsius, TemperatureUnit unit) => unit switch
    {
        TemperatureUnit.Fahrenheit => Math.Round(celsius * 9.0 / 5.0 + 32, 1),
        _ => celsius
    };
}

namespace FamilyHQ.WebUi.Services;

using FamilyHQ.Core.Enums;

public interface IWeatherOverrideService
{
    bool IsActive { get; }
    WeatherCondition? ActiveCondition { get; }
    bool IsWindy { get; }

    event Action? OnOverrideChanged;

    void Activate(WeatherCondition condition);
    void SelectCondition(WeatherCondition condition);
    void SetWindy(bool isWindy);
    void Deactivate();
}

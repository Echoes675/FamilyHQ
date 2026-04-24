namespace FamilyHQ.WebUi.Services;

using FamilyHQ.Core.Enums;

public class WeatherOverrideService : IWeatherOverrideService
{
    public bool IsActive { get; private set; }
    public WeatherCondition? ActiveCondition { get; private set; }
    public bool IsWindy { get; private set; }

    public event Action? OnOverrideChanged;

    public void Activate(WeatherCondition condition)
    {
        var changed = !IsActive
                   || ActiveCondition != condition
                   || IsWindy;

        IsActive = true;
        ActiveCondition = condition;
        IsWindy = false;

        if (changed)
            OnOverrideChanged?.Invoke();
    }

    public void SelectCondition(WeatherCondition condition)
    {
        if (!IsActive) return;
        if (ActiveCondition == condition) return;

        ActiveCondition = condition;
        OnOverrideChanged?.Invoke();
    }

    public void SetWindy(bool isWindy)
    {
        if (!IsActive) return;
        if (IsWindy == isWindy) return;

        IsWindy = isWindy;
        OnOverrideChanged?.Invoke();
    }

    public void Deactivate()
    {
        if (!IsActive) return;

        IsActive = false;
        ActiveCondition = null;
        IsWindy = false;
        OnOverrideChanged?.Invoke();
    }
}

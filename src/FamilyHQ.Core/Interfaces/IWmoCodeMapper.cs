namespace FamilyHQ.Core.Interfaces;

using FamilyHQ.Core.Enums;

public interface IWmoCodeMapper
{
    // Returns false — with condition set to WeatherCondition.Unknown — when the WMO code
    // has no mapping, so the caller can report the gap instead of silently guessing.
    bool TryGetCondition(int wmoCode, out WeatherCondition condition);
}

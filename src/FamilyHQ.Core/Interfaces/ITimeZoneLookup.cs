namespace FamilyHQ.Core.Interfaces;

public interface ITimeZoneLookup
{
    string? GetTimeZone(double latitude, double longitude);
}

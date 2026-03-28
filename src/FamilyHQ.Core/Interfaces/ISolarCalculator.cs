namespace FamilyHQ.Core.Interfaces;

public interface ISolarCalculator
{
    /// <summary>
    /// Calculates sunrise and sunset times for a given date and location.
    /// Returns times in UTC.
    /// </summary>
    /// <param name="date">The date to calculate for</param>
    /// <param name="latitude">Latitude in decimal degrees (positive = North)</param>
    /// <param name="longitude">Longitude in decimal degrees (positive = East)</param>
    /// <returns>Tuple of (sunrise UTC, sunset UTC), or null if sun doesn't rise/set (polar regions)</returns>
    (TimeOnly Sunrise, TimeOnly Sunset)? Calculate(DateOnly date, double latitude, double longitude);
}

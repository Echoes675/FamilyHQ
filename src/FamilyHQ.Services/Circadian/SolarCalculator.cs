using FamilyHQ.Core.Interfaces;

namespace FamilyHQ.Services.Circadian;

/// <summary>
/// Implements the NOAA Solar Calculator algorithm to compute sunrise and sunset times.
/// Based on: https://gml.noaa.gov/grad/solcalc/calcdetails.html
/// Pure mathematical computation — no external API required.
/// </summary>
public sealed class SolarCalculator : ISolarCalculator
{
    public (TimeOnly Sunrise, TimeOnly Sunset)? Calculate(DateOnly date, double latitude, double longitude)
    {
        // Julian Day Number
        var jd = DateToJulianDay(date);
        
        // Julian Century
        var t = (jd - 2451545.0) / 36525.0;
        
        // Geometric mean longitude of the sun (degrees)
        var l0 = (280.46646 + t * (36000.76983 + t * 0.0003032)) % 360;
        
        // Geometric mean anomaly of the sun (degrees)
        var m = 357.52911 + t * (35999.05029 - 0.0001537 * t);
        var mRad = ToRadians(m);
        
        // Equation of center
        var c = Math.Sin(mRad) * (1.914602 - t * (0.004817 + 0.000014 * t))
              + Math.Sin(2 * mRad) * (0.019993 - 0.000101 * t)
              + Math.Sin(3 * mRad) * 0.000289;
        
        // Sun's true longitude (degrees)
        var sunLon = l0 + c;
        
        // Apparent longitude (degrees)
        var omega = 125.04 - 1934.136 * t;
        var lambda = sunLon - 0.00569 - 0.00478 * Math.Sin(ToRadians(omega));
        
        // Mean obliquity of the ecliptic (degrees)
        var epsilon0 = 23.0 + (26.0 + (21.448 - t * (46.8150 + t * (0.00059 - t * 0.001813))) / 60.0) / 60.0;
        
        // Corrected obliquity
        var epsilon = epsilon0 + 0.00256 * Math.Cos(ToRadians(omega));
        
        // Sun's declination (degrees)
        var decl = ToDegrees(Math.Asin(Math.Sin(ToRadians(epsilon)) * Math.Sin(ToRadians(lambda))));
        
        // Equation of time (minutes)
        var y = Math.Tan(ToRadians(epsilon / 2)) * Math.Tan(ToRadians(epsilon / 2));
        var l0Rad = ToRadians(l0);
        var mRad2 = ToRadians(m);
        var eot = 4 * ToDegrees(
            y * Math.Sin(2 * l0Rad)
            - 2 * 0.016708634 * Math.Sin(mRad2)
            + 4 * 0.016708634 * y * Math.Sin(mRad2) * Math.Cos(2 * l0Rad)
            - 0.5 * y * y * Math.Sin(4 * l0Rad)
            - 1.25 * 0.016708634 * 0.016708634 * Math.Sin(2 * mRad2));
        
        // Hour angle sunrise (degrees)
        var latRad = ToRadians(latitude);
        var declRad = ToRadians(decl);
        var cosHa = (Math.Cos(ToRadians(90.833)) / (Math.Cos(latRad) * Math.Cos(declRad)))
                  - Math.Tan(latRad) * Math.Tan(declRad);
        
        // Check for polar day/night
        if (cosHa < -1 || cosHa > 1) return null;
        
        var ha = ToDegrees(Math.Acos(cosHa));
        
        // Solar noon (minutes from midnight UTC)
        var solarNoon = (720 - 4 * longitude - eot) / 1440.0 * 24 * 60; // in minutes
        
        // Sunrise and sunset (minutes from midnight UTC)
        var sunriseMinutes = solarNoon - ha * 4;
        var sunsetMinutes = solarNoon + ha * 4;
        
        return (MinutesToTimeOnly(sunriseMinutes), MinutesToTimeOnly(sunsetMinutes));
    }
    
    private static double DateToJulianDay(DateOnly date)
    {
        var y = date.Year;
        var m = date.Month;
        var d = date.Day;
        
        if (m <= 2) { y--; m += 12; }
        
        var a = Math.Floor(y / 100.0);
        var b = 2 - a + Math.Floor(a / 4.0);
        
        return Math.Floor(365.25 * (y + 4716)) + Math.Floor(30.6001 * (m + 1)) + d + b - 1524.5;
    }
    
    private static TimeOnly MinutesToTimeOnly(double totalMinutes)
    {
        // Clamp to valid range
        totalMinutes = ((totalMinutes % 1440) + 1440) % 1440;
        var hours = (int)(totalMinutes / 60);
        var minutes = (int)(totalMinutes % 60);
        var seconds = (int)((totalMinutes * 60) % 60);
        return new TimeOnly(hours, minutes, seconds);
    }
    
    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
    private static double ToDegrees(double radians) => radians * 180.0 / Math.PI;
}

namespace FamilyHQ.Core.Models;

public enum CircadianState
{
    Dawn,
    Day,
    Dusk,
    Night
}

/// <summary>
/// Stores the computed sunrise/sunset boundaries for a specific date and location.
/// Computed daily by CircadianStateService using the NOAA solar algorithm.
/// </summary>
public class CircadianBoundaries
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    
    /// <summary>Astronomical sunrise time (UTC)</summary>
    public TimeOnly SunriseUtc { get; set; }
    
    /// <summary>Astronomical sunset time (UTC)</summary>
    public TimeOnly SunsetUtc { get; set; }
    
    /// <summary>Dawn starts 30 minutes before sunrise</summary>
    public TimeOnly DawnStartUtc => SunriseUtc.AddMinutes(-30);
    
    /// <summary>Dusk ends 30 minutes after sunset</summary>
    public TimeOnly DuskEndUtc => SunsetUtc.AddMinutes(30);
    
    public DateTimeOffset ComputedAt { get; set; }
    
    /// <summary>
    /// Determines the circadian state for a given UTC time.
    /// </summary>
    public CircadianState GetStateForTime(TimeOnly utcTime)
    {
        if (utcTime >= DawnStartUtc && utcTime < SunriseUtc) return CircadianState.Dawn;
        if (utcTime >= SunriseUtc && utcTime < SunsetUtc) return CircadianState.Day;
        if (utcTime >= SunsetUtc && utcTime < DuskEndUtc) return CircadianState.Dusk;
        return CircadianState.Night;
    }
}

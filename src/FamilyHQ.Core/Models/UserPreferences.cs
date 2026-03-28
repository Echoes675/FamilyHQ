namespace FamilyHQ.Core.Models;

/// <summary>
/// User-specific display preferences for the kiosk dashboard.
/// Stored in the database per user, with LocalStorage as the primary cache.
/// </summary>
public class UserPreferences
{
    public int Id { get; set; }
    
    /// <summary>The user's Google subject ID (from JWT sub claim)</summary>
    public string UserId { get; set; } = string.Empty;
    
    /// <summary>
    /// Event density: how many events to show per cell in month view (1–3).
    /// Default: 2
    /// </summary>
    public int EventDensity { get; set; } = 2;
    
    /// <summary>
    /// Ordered list of calendar IDs for column display order in month/week views.
    /// Serialized as JSON array string.
    /// </summary>
    public string? CalendarColumnOrder { get; set; }
    
    /// <summary>
    /// Custom color overrides for calendars, keyed by calendar ID.
    /// Serialized as JSON object string: { "calendarId": "#hexcolor" }
    /// </summary>
    public string? CalendarColorOverrides { get; set; }
    
    public DateTimeOffset LastModified { get; set; }
}

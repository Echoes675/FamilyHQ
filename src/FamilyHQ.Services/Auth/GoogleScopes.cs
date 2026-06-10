namespace FamilyHQ.Services.Auth;

public static class GoogleScopes
{
    /// <summary>The full read/write Google Calendar scope FamilyHQ requires.</summary>
    public const string Calendar = "https://www.googleapis.com/auth/calendar";

    /// <summary>
    /// True when the space-delimited granted-scope string from Google's token endpoint includes the
    /// full <see cref="Calendar"/> scope as an exact token. A near-miss (e.g. "calendar.readonly")
    /// does not satisfy this; null/empty returns false (fail safe to "not granted"). (FHQ-60)
    /// </summary>
    public static bool GrantsCalendar(string? grantedScope)
    {
        if (string.IsNullOrWhiteSpace(grantedScope)) return false;
        var tokens = grantedScope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return System.Array.IndexOf(tokens, Calendar) >= 0;
    }
}

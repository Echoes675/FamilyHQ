using System.Security.Cryptography;
using System.Text;

namespace FamilyHQ.Services.Calendar;

/// <summary>
/// Utility for computing stable hashes of event content.
/// Used to detect self-generated echo webhooks from Google Calendar.
/// </summary>
public static class EventContentHash
{
    /// <summary>
    /// Computes a stable hex hash of the key event fields.
    /// Used to detect self-generated echo webhooks.
    /// </summary>
    public static string Compute(
        string title,
        DateTimeOffset start,
        DateTimeOffset end,
        bool isAllDay,
        string? description)
    {
        var desc = string.IsNullOrEmpty(description) ? "" : description;
        // Use ASCII unit separator (\u001F) as delimiter — cannot appear in user-entered calendar text,
        // preventing hash collisions from pipe characters in titles or descriptions.
        const char Sep = '\u001F';
        var input = $"{title}{Sep}{start.ToUniversalTime():O}{Sep}{end.ToUniversalTime():O}{Sep}{isAllDay}{Sep}{desc}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

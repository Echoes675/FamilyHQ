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
        var input = $"{title}|{start.ToUniversalTime():O}|{end.ToUniversalTime():O}|{isAllDay}|{desc}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

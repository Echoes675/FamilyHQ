using System.Text;
using System.Text.Json;

namespace FamilyHQ.WebUi.Services.Auth;

/// <summary>
/// Pure client-side decoder for the FamilyHQ JWT payload. Performs NO signature validation —
/// the server does that on every request; this only reads claims for display and for the
/// sliding-renewal decision (FHQ-126). Malformed tokens yield empty claims.
/// </summary>
public static class JwtTokenDecoder
{
    private static readonly JwtTokenClaims Empty = new(null, null, null);

    /// <summary>
    /// Decodes the payload of a JWT and extracts the "sub", "name"/"unique_name" and "exp" claims.
    /// </summary>
    public static JwtTokenClaims Decode(string token)
    {
        try
        {
            // JWT format: header.payload.signature
            var parts = token.Split('.');
            if (parts.Length != 3)
                return Empty;

            // Decode the payload (second part), re-adding base64 padding if needed
            var payload = parts[1];
            var padding = 4 - (payload.Length % 4);
            if (padding != 4)
                payload += new string('=', padding);

            var jsonBytes = Convert.FromBase64String(payload.Replace('-', '+').Replace('_', '/'));
            var jsonString = Encoding.UTF8.GetString(jsonBytes);

            using var doc = JsonDocument.Parse(jsonString);
            var root = doc.RootElement;

            string? userId = null;
            if (root.TryGetProperty("sub", out var subElement))
            {
                userId = subElement.GetString();
            }

            // Username — try "name" first, then "unique_name"
            string? username = null;
            if (root.TryGetProperty("name", out var nameElement))
            {
                username = nameElement.GetString();
            }
            if (string.IsNullOrEmpty(username) && root.TryGetProperty("unique_name", out var uniqueNameElement))
            {
                username = uniqueNameElement.GetString();
            }

            // Expiry — unix seconds; anything non-numeric or out of range stays null ("expiring")
            DateTimeOffset? expiresAtUtc = null;
            if (root.TryGetProperty("exp", out var expElement)
                && expElement.ValueKind == JsonValueKind.Number
                && expElement.TryGetInt64(out var expUnixSeconds))
            {
                expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(expUnixSeconds);
            }

            return new JwtTokenClaims(userId, username, expiresAtUtc);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            // Malformed token (bad base64, bad JSON, absurd exp). No logger here by design — this
            // is a pure helper; callers log per their context (unauthenticated vs. renew-now).
            return Empty;
        }
    }
}

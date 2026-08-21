namespace FamilyHQ.Core.Logging;

using System.Security.Cryptography;
using System.Text;
using FamilyHQ.Core.Interfaces;
using Microsoft.Extensions.Logging;

/// <summary>
/// FHQ-166. Redacts a PII-bearing identifier to the leading bytes of an HMAC-SHA256 taken under a
/// deployment-wide salt, rendered as lowercase hex.
/// <para>
/// <b>Why salted.</b> A bare hash of an email address is reversible in practice: the address space
/// a family draws from is tiny, so anyone holding the log and a candidate list can confirm a match
/// by hashing the candidates. The salt is what makes the token a label rather than a lookup key.
/// </para>
/// <para>
/// <b>Where the salt comes from.</b> Configuration key <see cref="SaltConfigurationKey"/>, supplied
/// as an environment variable in deployed environments and via user secrets locally — never a
/// literal in this repository, because a salt committed next to the code it salts is not a secret.
/// </para>
/// <para>
/// <b>When the salt is absent.</b> A random per-process salt is generated and the condition is
/// reported once at <see cref="LogLevel.Warning"/>. The guarantee that must never bend is
/// non-reversibility, and a random salt strengthens it; what degrades is correlation, which becomes
/// per-process rather than per-deployment. Failing startup instead would turn a redaction change
/// into a deployment break in every environment that has not set the key yet, and refusing to
/// redact would be worse than either.
/// </para>
/// </summary>
public sealed class SaltedHashPiiRedactor : IPiiRedactor
{
    /// <summary>Configuration key holding the deployment-wide redaction salt.</summary>
    public const string SaltConfigurationKey = "Logging:Redaction:Salt";

    /// <summary>Token substituted for a null or empty value, so the log line still reads sensibly.</summary>
    public const string AbsentValueToken = "(none)";

    /// <summary>
    /// Hex characters kept from the digest. 16 hex characters is 64 bits — far beyond collision
    /// range for the handful of calendars one household has, while staying short enough to read.
    /// </summary>
    private const int TokenLength = 16;

    private const int GeneratedSaltBytes = 32;

    private readonly byte[] _salt;

    public SaltedHashPiiRedactor(string? salt, ILogger<SaltedHashPiiRedactor> logger)
    {
        if (!string.IsNullOrWhiteSpace(salt))
        {
            _salt = Encoding.UTF8.GetBytes(salt);
            return;
        }

        // Not wrapped behind an injectable clock/RNG seam: the observable contract of this branch is
        // "two instances do not agree", which a test asserts directly. A substitutable RNG would let
        // a test hand back a fixed salt and quietly assert the opposite of what production does.
        _salt = RandomNumberGenerator.GetBytes(GeneratedSaltBytes);
        logger.LogWarning(
            "No log-redaction salt is configured at {ConfigurationKey}; a random per-process salt was generated. " +
            "Redaction is still non-reversible, but a given calendar redacts to a different token in every " +
            "process, so it cannot be correlated across restarts or replicas until the salt is configured.",
            SaltConfigurationKey);
    }

    public string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return AbsentValueToken;
        }

        var digest = HMACSHA256.HashData(_salt, Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(digest)[..TokenLength];
    }
}

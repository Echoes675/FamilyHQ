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
/// Generate one with <c>openssl rand -base64 32</c>.
/// </para>
/// <para>
/// <b>When a salt IS supplied it must be long enough to be worth having.</b> A one-character salt
/// is guessed in a single pass over the alphabet, which puts the household candidate list straight
/// back in play — the exact attack the paragraph above says the salt prevents. Construction
/// therefore fails on a supplied salt shorter than <see cref="MinimumSaltLength"/>, and
/// <c>AddFamilyHqServices</c> performs the same check eagerly at boot (the FHQ-91 precedent), so a
/// misconfiguration surfaces at startup rather than at the first log line that needs redacting.
/// </para>
/// <para>
/// <b>When the salt is absent.</b> A random per-process salt is generated and the condition is
/// reported once at <see cref="LogLevel.Warning"/>. The guarantee that must never bend is
/// non-reversibility, and a random salt strengthens it; what degrades is correlation, which becomes
/// per-process rather than per-deployment. Failing startup instead would turn a redaction change
/// into a deployment break in every environment that has not set the key yet, and refusing to
/// redact would be worse than either. Note the asymmetry with a too-short salt: absent is a state
/// this class can degrade into safely, whereas short is an active claim of protection it does not
/// provide.
/// </para>
/// <para>
/// <b>The crypto primitives are called statically on purpose.</b> They are pure framework functions
/// with no ambient non-determinism, and substituting them would let a test hand back a fixed digest
/// or a fixed salt and assert the opposite of what production does. See the "Wrapping static calls"
/// exemption in <c>.agent/skills/coding-standards/SKILL.md</c>.
/// </para>
/// </summary>
public sealed class SaltedHashPiiRedactor : IPiiRedactor
{
    /// <summary>Configuration key holding the deployment-wide redaction salt.</summary>
    public const string SaltConfigurationKey = "Security:RedactionSalt";

    /// <summary>
    /// Shortest salt accepted when one is supplied, in characters. It matches the entropy of the
    /// generated fallback (<see cref="GeneratedSaltBytes"/> random bytes) and is what
    /// <c>openssl rand -base64 32</c> produces once base64-encoded, so the documented way to make a
    /// salt satisfies it by construction.
    /// </summary>
    public const int MinimumSaltLength = 32;

    /// <summary>Token substituted for a null or empty value, so the log line still reads sensibly.</summary>
    public const string AbsentValueToken = "(absent)";

    /// <summary>
    /// Hex characters kept from the digest. 16 hex characters is 64 bits — far beyond collision
    /// range for the handful of calendars one household has, while staying short enough to read.
    /// </summary>
    private const int TokenLength = 16;

    private const int GeneratedSaltBytes = 32;

    private readonly byte[] _salt;

    public SaltedHashPiiRedactor(string? salt, ILogger<SaltedHashPiiRedactor> logger)
    {
        // The null/blank test differs from Redact's on purpose. Here, "   " is a configuration
        // mistake — an environment variable that was set but carries no entropy — and is treated as
        // no salt at all. In Redact, a whitespace VALUE is a real (if odd) value and must still be
        // hashed rather than collapsed into the absent token alongside genuine nulls.
        if (!string.IsNullOrWhiteSpace(salt))
        {
            ValidateSalt(salt);
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

    /// <summary>
    /// Throws when a salt was supplied but is too short to be one. A null, empty or whitespace salt
    /// is NOT a failure — that is the documented degraded mode, handled by the constructor.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The supplied salt is shorter than <see cref="MinimumSaltLength"/>. The message names the
    /// configuration key and the length found, never the salt itself.
    /// </exception>
    public static void ValidateSalt(string? salt)
    {
        if (string.IsNullOrWhiteSpace(salt) || salt.Length >= MinimumSaltLength)
        {
            return;
        }

        throw new ArgumentException(
            $"The log-redaction salt configured at {SaltConfigurationKey} is {salt.Length} characters long; " +
            $"at least {MinimumSaltLength} are required. A short salt is guessable, which puts every " +
            "redacted token back within reach of anyone holding the logs and a list of candidate " +
            "addresses. Generate one with `openssl rand -base64 32`, or leave the key unset to fall " +
            "back to a random per-process salt.",
            nameof(salt));
    }

    public string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return AbsentValueToken;
        }

        // Salt is the KEY and the value is the MESSAGE, not the other way round: keying by the
        // secret is what makes a candidate address unconfirmable without it.
        var digest = HMACSHA256.HashData(_salt, Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(digest)[..TokenLength];
    }
}

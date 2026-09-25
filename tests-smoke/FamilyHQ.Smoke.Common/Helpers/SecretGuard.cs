namespace FamilyHQ.Smoke.Common.Helpers;

/// <summary>
/// The last line of defence for FHQ-141 principle 7: the JWT, the Google access token and the
/// issue-token shared secret never appear in a log line, on the console, or in an archived artifact.
/// <para>
/// Discipline at the call sites is the primary defence — nothing in this suite passes a credential to
/// a log template. This exists for the one place discipline cannot reach: the body of a failed HTTP
/// response, which the suite quotes so a failure is diagnosable, and which an upstream proxy is free
/// to echo the request into. Registered secrets are replaced with a fixed marker before any such text
/// is emitted.
/// </para>
/// <para>
/// Registrations are process-wide and additive; a secret is never read back out, only matched.
/// </para>
/// </summary>
public static class SecretGuard
{
    /// <summary>What a redacted secret is replaced with. Distinctive so a leak test can assert on it.</summary>
    public const string Marker = "[redacted]";

    private const int MinimumRegisterableLength = 8;

    private static readonly Lock Gate = new();
    private static readonly List<string> Secrets = [];

    /// <summary>
    /// Registers a value never to be emitted. Short and empty values are ignored: they are not
    /// credentials, and a two-character "secret" would redact half of every diagnostic message.
    /// </summary>
    public static void Register(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < MinimumRegisterableLength)
        {
            return;
        }

        lock (Gate)
        {
            if (!Secrets.Contains(secret, StringComparer.Ordinal))
            {
                Secrets.Add(secret);
            }
        }
    }

    /// <summary>Replaces every registered secret in <paramref name="text"/> with <see cref="Marker"/>.</summary>
    public static string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string[] snapshot;
        lock (Gate)
        {
            snapshot = [.. Secrets];
        }

        var scrubbed = text;
        foreach (var secret in snapshot)
        {
            scrubbed = scrubbed.Replace(secret, Marker, StringComparison.Ordinal);
        }

        return scrubbed;
    }
}

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using FamilyHQ.WebApi.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FamilyHQ.WebApi.Auth;

/// <summary>
/// Authenticates the smoke pipeline by its shared secret (FHQ-139), presented as
/// <c>Authorization: Bearer &lt;secret&gt;</c>.
/// <para>
/// <b>Why a separate scheme.</b> The secret authenticates a machine, not a person. Keeping it in its
/// own scheme — rather than teaching the FamilyHQ JWT scheme a second credential — means the secret
/// can never satisfy a user login, and the principal it produces carries no <c>sub</c> claim, so
/// <c>ICurrentUserService</c> cannot resolve it as a family member. Conversely, a valid FamilyHQ user
/// JWT cannot satisfy this scheme, and the <c>PreprodSmokeAccess</c> policy accepts no other.
/// </para>
/// <para>
/// <b>Why every failure is a 404.</b> Both the challenge (no or wrong credentials) and the forbid (a
/// policy requirement failed) paths answer 404 with no <c>WWW-Authenticate</c> header, so a probe
/// cannot tell an unreachable smoke endpoint from a route that does not exist. That is also why the
/// endpoints are deliberately NOT rate limited: a 429 would announce the route's existence, and the
/// route is only reachable at all in a LAN-only environment.
/// </para>
/// </summary>
public class SmokeClientAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "SmokeClient";

    private const string BearerPrefix = "Bearer ";

    private readonly IOptions<IssueTokenEndpointOptions> _endpointOptions;

    public SmokeClientAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<IssueTokenEndpointOptions> endpointOptions)
        : base(options, logger, encoder)
    {
        _endpointOptions = endpointOptions;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var configuredSecret = _endpointOptions.Value.Secret;
        if (string.IsNullOrWhiteSpace(configuredSecret))
        {
            // Fail closed. Not a Warning: with the endpoints switched off this is the normal state of
            // every environment, and warning per request would be pure noise. Enabled-without-a-secret
            // cannot reach here — PreprodSmokeAccessGuard refuses the boot.
            Logger.LogDebug("Smoke client authentication skipped — no shared secret is configured.");
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!TryReadPresentedSecret(out var presentedSecret))
        {
            Logger.LogDebug(
                "Smoke client authentication skipped — no single bearer credential was presented for {RequestPath}.",
                Request.Path);
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!SecretsMatch(presentedSecret, configuredSecret))
        {
            // Audit line. Neither secret is logged — the path and the caller's address are enough to
            // correlate, and the values themselves are exactly what must never reach Seq.
            Logger.LogWarning(
                "Smoke client authentication failed for {RequestPath} — the presented shared secret did not match.",
                Request.Path);
            return Task.FromResult(AuthenticateResult.Fail("Invalid smoke client credentials."));
        }

        Logger.LogInformation(
            "Smoke client authenticated for {RequestPath}.", Request.Path);

        // No sub claim, by design (see the class remarks): an authenticated machine caller, not a user.
        var identity = new ClaimsIdentity(SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>
    /// No credentials, or credentials that do not authenticate: answer 404, advertising nothing.
    /// </summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Authenticated, but a <c>PreprodSmokeAccess</c> requirement failed (wrong tier, endpoint
    /// disabled, or a user id that is not the smoke account): answer 404, the same as everything else.
    /// </summary>
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }

    private bool TryReadPresentedSecret(out string presentedSecret)
    {
        presentedSecret = string.Empty;

        // A multi-valued Authorization header is ambiguous — which value did the caller mean? — so it
        // is refused rather than guessed at.
        if (!Request.Headers.TryGetValue("Authorization", out var headerValues) || headerValues.Count != 1)
            return false;

        var header = headerValues[0];
        if (header is null || !header.StartsWith(BearerPrefix, StringComparison.Ordinal))
            return false;

        presentedSecret = header[BearerPrefix.Length..];
        return presentedSecret.Length > 0;
    }

    /// <summary>
    /// Constant-time comparison. Both values are hashed first so that the comparison is over two
    /// fixed-length digests: <see cref="CryptographicOperations.FixedTimeEquals"/> returns early for
    /// inputs of differing length, which would leak the configured secret's length.
    /// <para>
    /// <c>SHA256.HashData</c> and <c>Encoding.UTF8.GetBytes</c> are pure framework functions, so they
    /// are exempt from the injectable-seam rule (coding-standards, FHQ-166): substituting them could
    /// only be used to assert the opposite of what production computes.
    /// </para>
    /// </summary>
    private static bool SecretsMatch(string presented, string configured) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(presented)),
            SHA256.HashData(Encoding.UTF8.GetBytes(configured)));
}

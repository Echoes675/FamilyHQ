using FamilyHQ.Data;
using FamilyHQ.WebApi.Auth;
using FamilyHQ.WebApi.Models;
using FamilyHQ.WebApi.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace FamilyHQ.WebApi.Controllers;

/// <summary>
/// Issues a JWT for a known user without driving the Google OAuth UI. Gated by a shared
/// secret and intended for unattended smoke-test pipelines. See <see cref="IssueTokenEndpointOptions"/>
/// and the FHQ-23 spec for the layered guards that protect against accidental prod exposure.
///
/// Note on the spec's "Conditional route registration" (§4.1): rather than conditionally calling
/// <c>MapControllers</c>, this controller is always registered and the
/// <see cref="IssueTokenEndpointOptions.Enabled"/> flag is enforced at runtime by returning
/// <see cref="NotFoundResult"/>. The real defences against
/// accidental prod exposure are the production-deploy validation (Task 10) and the startup
/// assertion (Task 8); the runtime 404 here is the last line of defence, not the first.
/// </summary>
[ApiController]
[Route("api/auth/issue-token")]
public sealed class IssueTokenController : ControllerBase
{
    private const string GoogleProvider = "Google";

    private readonly IOptions<IssueTokenEndpointOptions> _options;
    private readonly FamilyHqDbContext _db;
    private readonly IJwtIssuer _jwtIssuer;
    private readonly ILogger<IssueTokenController> _logger;

    public IssueTokenController(
        IOptions<IssueTokenEndpointOptions> options,
        FamilyHqDbContext db,
        IJwtIssuer jwtIssuer,
        ILogger<IssueTokenController> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(jwtIssuer);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _db = db;
        _jwtIssuer = jwtIssuer;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> IssueToken([FromBody] IssueTokenRequest? request)
    {
        var options = _options.Value;

        // Guard 1: feature must be opted-in. Return framework-style 404 so callers cannot
        // distinguish "disabled" from "unknown route".
        if (!options.Enabled)
        {
            return NotFound();
        }

        // Guard 2: validate the request body BEFORE authenticating. A malformed body is a
        // structural request error and revealing it is acceptable. Doing this before auth
        // closes a secret-correctness oracle: if body-validation ran after auth, an attacker
        // could probe with a deliberately bad body and observe 400 only when their bearer
        // secret happened to be correct — turning the response code into a confirmation
        // signal. Order matters here for the spec's anti-enumeration guarantee.
        if (request is null || string.IsNullOrWhiteSpace(request.UserId))
        {
            return BadRequest(new { error = "userId is required." });
        }

        var callerIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Guard 3: validate the bearer secret with constant-time comparison. Mismatched or
        // missing secrets return 404 (not 401) so the endpoint exposes no enumeration signal.
        if (!TryAuthenticateCaller(options.Secret))
        {
            _logger.LogWarning(
                "auth.issue_token.unauthorised caller_ip={CallerIp}",
                callerIp);
            return NotFound();
        }

        // Lookup uses the same (UserId, Provider) pair the rest of the app keys on. Missing
        // user → 404, structurally identical to the unauthorised response.
        var exists = await _db.UserTokens
            .AsNoTracking()
            .AnyAsync(t => t.UserId == request.UserId && t.Provider == GoogleProvider);
        if (!exists)
        {
            _logger.LogWarning(
                "auth.issue_token.unknown_user user_id={UserId} caller_ip={CallerIp}",
                request.UserId,
                callerIp);
            return NotFound();
        }

        // Email is not persisted, so the token only carries sub/unique_name. That's sufficient
        // for downstream authorisation which already keys off the sub claim.
        var (token, expiresAt) = _jwtIssuer.Issue(request.UserId);

        _logger.LogInformation(
            "auth.issue_token.success user_id={UserId} caller_ip={CallerIp}",
            request.UserId,
            callerIp);

        return Ok(new IssueTokenResponse(token, expiresAt));
    }

    private bool TryAuthenticateCaller(string configuredSecret)
    {
        if (string.IsNullOrEmpty(configuredSecret))
        {
            // Refuse to authenticate against an unconfigured secret — fail closed.
            return false;
        }

        // Reject multi-valued or missing Authorization headers. A multi-valued header is
        // unusual enough to be ambiguous (which value did the caller mean?) and we'd rather
        // refuse than guess.
        if (!Request.Headers.TryGetValue("Authorization", out var headerValues) || headerValues.Count != 1)
        {
            return false;
        }

        var header = headerValues[0]!;
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var presented = header.AsSpan(prefix.Length).ToString();
        var presentedBytes = Encoding.UTF8.GetBytes(presented);
        var configuredBytes = Encoding.UTF8.GetBytes(configuredSecret);
        return CryptographicOperations.FixedTimeEquals(presentedBytes, configuredBytes);
    }
}

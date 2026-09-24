using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FamilyHQ.Smoke.Common.Configuration;
using FamilyHQ.Smoke.Common.Helpers;
using FamilyHQ.Smoke.Data.Models;

namespace FamilyHQ.Smoke.Data.Api;

/// <summary>
/// The two preprod smoke token endpoints (FHQ-139) — the only credentials this suite has, and the whole
/// reason it can run unattended.
/// <para>
/// <b>Secrets.</b> The shared secret, the JWT and the Google access token are registered with
/// <see cref="SecretGuard"/> the moment they exist, so that even the one place the suite quotes
/// untrusted text — the body of a failed response — cannot carry one into a log or an artifact. None of
/// the three is ever passed to a log template, returned in an exception message, or written to disk.
/// </para>
/// </summary>
public sealed class SmokeTokenClient : IDisposable
{
    private readonly SmokeConfiguration _configuration;
    private readonly HttpClient _httpClient;

    public SmokeTokenClient(SmokeConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _configuration = configuration;
        _httpClient = SmokeHttpClientFactory.Create(configuration, configuration.BaseUrl);
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", configuration.IssueTokenSecret);

        SecretGuard.Register(configuration.IssueTokenSecret);
    }

    /// <summary>Mints a FamilyHQ session JWT for the smoke account, so the kiosk can start signed in.</summary>
    public async Task<SmokeSessionToken> IssueSessionTokenAsync(CancellationToken ct = default)
    {
        using var response = await PostAsync("api/auth/issue-token", ct);
        var outcome = Classify(response.StatusCode);

        if (outcome != SmokeTokenOutcome.Issued)
        {
            return new SmokeSessionToken(outcome, null, (int)response.StatusCode);
        }

        var body = await ReadAsync<IssueTokenBody>(response, ct);
        if (string.IsNullOrWhiteSpace(body?.Token))
        {
            return new SmokeSessionToken(SmokeTokenOutcome.Unexpected, null, (int)response.StatusCode);
        }

        SecretGuard.Register(body.Token);
        return new SmokeSessionToken(SmokeTokenOutcome.Issued, body.Token, (int)response.StatusCode);
    }

    /// <summary>
    /// Refreshes the stored Google grant and returns a live access token — the suite's oracle credential.
    /// A revoked grant comes back as <see cref="SmokeTokenOutcome.ReauthRequired"/>, which is the one
    /// failure a smoke run must report rather than retry.
    /// </summary>
    public async Task<SmokeGoogleAccessToken> IssueGoogleAccessTokenAsync(CancellationToken ct = default)
    {
        using var response = await PostAsync("api/auth/issue-google-access-token", ct);
        var outcome = Classify(response.StatusCode);

        if (outcome != SmokeTokenOutcome.Issued)
        {
            return new SmokeGoogleAccessToken(outcome, null, null, (int)response.StatusCode);
        }

        var body = await ReadAsync<IssueGoogleAccessTokenBody>(response, ct);
        if (string.IsNullOrWhiteSpace(body?.AccessToken))
        {
            return new SmokeGoogleAccessToken(SmokeTokenOutcome.Unexpected, null, null, (int)response.StatusCode);
        }

        SecretGuard.Register(body.AccessToken);
        return new SmokeGoogleAccessToken(
            SmokeTokenOutcome.Issued, body.AccessToken, body.ExpiresAt, (int)response.StatusCode);
    }

    public void Dispose() => _httpClient.Dispose();

    private Task<HttpResponseMessage> PostAsync(string path, CancellationToken ct) =>
        _httpClient.PostAsJsonAsync(path, new { userId = _configuration.UserId }, SmokeJson.Options, ct);

    private static SmokeTokenOutcome Classify(HttpStatusCode status) => status switch
    {
        HttpStatusCode.OK => SmokeTokenOutcome.Issued,
        HttpStatusCode.Conflict => SmokeTokenOutcome.ReauthRequired,
        HttpStatusCode.NotFound => SmokeTokenOutcome.NotAvailable,
        HttpStatusCode.BadRequest => SmokeTokenOutcome.BadRequest,
        _ => SmokeTokenOutcome.Unexpected
    };

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(json, SmokeJson.Options);
    }

    private sealed record IssueTokenBody(string? Token);

    private sealed record IssueGoogleAccessTokenBody(string? AccessToken, DateTimeOffset? ExpiresAt);
}

namespace FamilyHQ.E2E.Common.Helpers;

using FamilyHQ.E2E.Common.Configuration;

/// <summary>
/// The WebApi origin for fetches issued **inside the browser** (`page.EvaluateAsync`).
/// <para>
/// Those fetches used to use a root-relative path (<c>fetch('/api/...')</c>), which silently assumes
/// the API is same-origin with the page. That holds in Docker, where Traefik routes <c>/api</c> to the
/// WebApi behind one hostname — and is false in the local dev-stack, where the WebUi is served by
/// <c>FamilyHQ.LocalWebHost</c> on 7154 and the WebApi listens on 7196. The static host answers an
/// unknown path with the SPA's <c>index.html</c> and a **200**, so the fetch neither throws nor fails
/// <c>resp.ok</c> — it returns HTML, and the caller dies on
/// <c>SyntaxError: Unexpected token '&lt;', "&lt;!DOCTYPE "... is not valid JSON</c>. That made every
/// weather scenario permanently unrunnable locally while passing in CI.
/// </para>
/// <para>
/// Resolving through <see cref="TestConfiguration.ApiBaseUrl"/> makes the address explicit in both
/// environments; <c>VersionFooterSteps</c> already addresses the API this way. Cross-origin is fine:
/// the WebApi's CORS policy allows the WebUi origin with any header and credentials, and the
/// Playwright context sets <c>IgnoreHTTPSErrors</c> for the dev certificate.
/// </para>
/// </summary>
public static class ApiOrigin
{
    private static readonly Lazy<string> LazyUrl = new(() =>
        ConfigurationLoader.Load().ApiBaseUrl.TrimEnd('/'));

    /// <summary>Absolute WebApi origin, no trailing slash (e.g. <c>https://localhost:7196</c>).</summary>
    public static string Url => LazyUrl.Value;

    /// <summary>Absolute URL for an API path. <paramref name="path"/> may start with or without '/'.</summary>
    public static string For(string path) => $"{Url}/{path.TrimStart('/')}";
}

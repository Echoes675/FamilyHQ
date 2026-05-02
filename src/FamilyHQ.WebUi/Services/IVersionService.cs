namespace FamilyHQ.WebUi.Services;

/// <summary>
/// Tracks the SemVer of the running Blazor WASM client and the latest version
/// reported by the backend, raising <see cref="UpdateAvailable"/> and reloading
/// the page when the two diverge.
/// </summary>
public interface IVersionService
{
    /// <summary>SemVer the WASM client was built with.</summary>
    string ClientVersion { get; }

    /// <summary>Latest SemVer reported by <c>/api/health</c>; <c>null</c> until the first successful fetch.</summary>
    string? ServerVersion { get; }

    /// <summary>Raised exactly once when a new server version is detected.</summary>
    event Action? UpdateAvailable;

    /// <summary>Performs the initial fetch of the server version. Errors are swallowed.</summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>Re-checks the server version and triggers an update flow when it differs from the client.</summary>
    Task CheckAsync(CancellationToken ct = default);
}

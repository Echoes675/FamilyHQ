namespace FamilyHQ.WebUi.Services;

/// <summary>
/// Result wrapper so the diagnostics page can distinguish a successful empty
/// response from a server / network failure — both look identical when a
/// nullable T or empty IReadOnlyList is returned directly.
/// </summary>
public record DiagnosticsLoadResult<T>(bool Loaded, T? Data)
{
    public static DiagnosticsLoadResult<T> Ok(T data) => new(true, data);
    public static DiagnosticsLoadResult<T> Failed() => new(false, default);
}

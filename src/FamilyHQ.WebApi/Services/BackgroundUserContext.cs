namespace FamilyHQ.WebApi.Services;

/// <summary>
/// Carries the authenticated user ID into background Task.Run contexts where
/// IHttpContextAccessor.HttpContext is null (no active HTTP request).
/// AsyncLocal values flow into child tasks via ExecutionContext capture.
/// </summary>
public static class BackgroundUserContext
{
    private static readonly AsyncLocal<string?> _userId = new();

    public static string? Current
    {
        get => _userId.Value;
        set => _userId.Value = value;
    }
}

namespace FamilyHQ.Core.Constants;

public static class CorrelationConstants
{
    public const string CorrelationIdHeaderName = "X-Correlation-Id";
    public const string SessionCorrelationIdHeaderName = "X-Session-Correlation-Id";

    /// <summary>
    /// Log-scope property name carrying the correlation id. Used by the HTTP middleware and by
    /// background-task scopes (FHQ-65) so Seq can filter by CorrelationId across both sources.
    /// </summary>
    public const string CorrelationIdLogProperty = "CorrelationId";

    /// <summary>
    /// Log-scope property name carrying the session correlation id (groups all requests from one
    /// client/kiosk session). Single-sourced alongside <see cref="CorrelationIdLogProperty"/> (FHQ-64).
    /// </summary>
    public const string SessionCorrelationIdLogProperty = "SessionCorrelationId";
}

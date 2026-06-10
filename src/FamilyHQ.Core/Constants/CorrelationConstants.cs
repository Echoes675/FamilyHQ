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
}

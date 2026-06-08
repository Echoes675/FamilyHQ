namespace FamilyHQ.Services.Auth;

/// <summary>
/// Thrown when Google rejects events.watch because the target calendar structurally cannot have push
/// notifications (reason "pushNotSupportedForRequestedResource") — e.g. read-only/subscribed calendars.
/// A benign, permanent condition: callers skip the calendar rather than treating it as an error.
/// </summary>
public class WebhookNotSupportedException : Exception
{
    public string Operation { get; }
    public string? Reason { get; }
    public string? ResponseBody { get; }

    public WebhookNotSupportedException(string operation, string? reason, string? responseBody)
        : base($"Google {operation} reports push notifications are not supported for this resource ({reason ?? "no reason"}).")
    {
        Operation = operation;
        Reason = reason;
        ResponseBody = responseBody;
    }
}

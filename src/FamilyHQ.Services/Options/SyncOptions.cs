using FamilyHQ.Services.Calendar;

namespace FamilyHQ.Services.Options;

public class SyncOptions
{
    public const string SectionName = "Sync";

    public TimeSpan PeriodicSyncInterval { get; set; } = TimeSpan.FromHours(1);
    public bool WebhookRegistrationEnabled { get; set; } = true;
    public TimeSpan WebhookRenewalInterval { get; set; } = TimeSpan.FromDays(6);
    public string? WebhookBaseUrl { get; set; }

    /// <summary>Worker poll backstop interval (also the max latency if a signal is missed).</summary>
    public TimeSpan WorkerPollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>InProgress jobs older than this are treated as orphaned (crash) and re-queued.</summary>
    public TimeSpan OrphanRecoveryThreshold { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Max attempts before a transient failure becomes terminal Failed.</summary>
    public int MaxSyncAttempts { get; set; } = 5;

    /// <summary>Base seconds for exponential backoff between retryable attempts.</summary>
    public int RetryBackoffBaseSeconds { get; set; } = 2;

    /// <summary>Terminal jobs older than this are pruned.</summary>
    public TimeSpan TerminalJobRetention { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    /// FHQ-196. Fail-fast guard, called at startup so a bad registration address surfaces at boot.
    /// <para>
    /// Without it the failure is invisible: <c>WebhookRegistrationService</c> logs one warning per
    /// calendar and carries on, so a deployment that forgot <c>Sync:WebhookBaseUrl</c> looks
    /// healthy while no push notification ever arrives.
    /// </para>
    /// <para>
    /// <b>http is deliberately allowed.</b> Dev and staging register against the Simulator at
    /// <c>http://webapi:8080</c> inside the compose network. What is rejected is an address that is
    /// not an absolute http(s) URL — including <c>webapi:8080</c>, which parses as an absolute URI
    /// whose scheme is <c>webapi</c> and would be handed to Google verbatim.
    /// </para>
    /// </summary>
    public void Validate()
    {
        if (!WebhookRegistrationEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(WebhookBaseUrl))
            throw new InvalidOperationException(
                $"{nameof(SyncOptions)}.{nameof(WebhookBaseUrl)} must be configured when " +
                $"{nameof(WebhookRegistrationEnabled)} is true; Google watch channels have nowhere to post to.");

        // Masked: the value may be a RelayRobin address, and an exception message reaches Seq via
        // whatever logs the startup failure.
        if (!Uri.TryCreate(WebhookBaseUrl, UriKind.Absolute, out var address)
            || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException(
                $"{nameof(SyncOptions)}.{nameof(WebhookBaseUrl)} must be an absolute http or https URL " +
                $"(was '{WebhookAddress.Mask(WebhookBaseUrl)}').");
    }
}

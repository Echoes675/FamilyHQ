using FamilyHQ.Services.Calendar;

namespace FamilyHQ.Services.Options;

public class SyncOptions
{
    public const string SectionName = "Sync";

    public TimeSpan PeriodicSyncInterval { get; set; } = TimeSpan.FromHours(1);
    public bool WebhookRegistrationEnabled { get; set; } = true;

    /// <summary>
    /// FHQ-213. How often the renewal pass wakes and asks "is any channel inside its final
    /// <see cref="WebhookRegistrationService.RenewalWindow"/>?" — a poll cadence, not a renewal period.
    /// <para>
    /// It must stay shorter than that window. Google's channels last ~7 days from registration and
    /// are only re-registered inside their last 24 hours, so the one thing that guarantees a pass
    /// lands inside that window — whenever the process happened to start — is polling more often
    /// than the window is wide. A pass with nothing due costs one query per calendar and no Google
    /// call, so an hour is cheap.
    /// </para>
    /// </summary>
    public TimeSpan WebhookRenewalPollInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// FHQ-213. Superseded by <see cref="WebhookRenewalPollInterval"/> and deliberately unused. Under
    /// the old design this was how long the renewal loop slept between passes, measured from process
    /// start — which is the bug: Google's channel clock runs from registration, so a restart reset
    /// this timer and the next pass could fall after the expiry it existed to prevent.
    /// <para>
    /// Kept only so a deployment whose env file still sets <c>Sync__WebhookRenewalInterval</c> binds
    /// and boots unchanged. The value is ignored and <c>WebhookRenewalService</c> warns once at
    /// startup so the stale key gets removed. Nullable so "operator set it" is distinguishable from
    /// "nobody set it".
    /// </para>
    /// </summary>
    public TimeSpan? WebhookRenewalInterval { get; set; }

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
        // FHQ-213: checked whether or not registration is enabled — a cadence that cannot work is a
        // deployment mistake either way, and boot is the only cheap place to say so.
        if (WebhookRenewalPollInterval <= TimeSpan.Zero)
            throw new InvalidOperationException(
                $"{nameof(SyncOptions)}.{nameof(WebhookRenewalPollInterval)} must be greater than zero " +
                $"(was {WebhookRenewalPollInterval}); a non-positive cadence turns the renewal pass into a spin loop.");

        if (WebhookRenewalPollInterval >= Calendar.WebhookRegistrationService.RenewalWindow)
            throw new InvalidOperationException(
                $"{nameof(SyncOptions)}.{nameof(WebhookRenewalPollInterval)} must be shorter than the " +
                $"{Calendar.WebhookRegistrationService.RenewalWindow.TotalHours:0}-hour re-registration window " +
                $"(was {WebhookRenewalPollInterval}); a channel would otherwise be able to expire between passes, " +
                "which is the defect FHQ-213 fixed.");

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

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
}

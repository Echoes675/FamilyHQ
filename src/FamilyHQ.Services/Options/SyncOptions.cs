namespace FamilyHQ.Services.Options;

public class SyncOptions
{
    public const string SectionName = "Sync";

    public TimeSpan PeriodicSyncInterval { get; set; } = TimeSpan.FromHours(1);
    public bool WebhookRegistrationEnabled { get; set; } = true;
    public TimeSpan WebhookRenewalInterval { get; set; } = TimeSpan.FromDays(6);
    public string? WebhookBaseUrl { get; set; }
}

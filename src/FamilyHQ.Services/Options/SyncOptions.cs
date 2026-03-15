namespace FamilyHQ.Services.Options;

public class SyncOptions
{
    public const string SectionName = "Sync";

    public TimeSpan PeriodicSyncInterval { get; set; } = TimeSpan.FromHours(1);
}

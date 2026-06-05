namespace FamilyHQ.Services.Options;

public class DayThemeOptions
{
    public const string SectionName = "DayTheme";

    /// <summary>
    /// Backoff applied before the scheduler retries after a loop iteration fails. Prevents a hot loop
    /// on a persistent fault while keeping the background service alive (the host is never stopped).
    /// </summary>
    public TimeSpan LoopErrorBackoff { get; set; } = TimeSpan.FromMinutes(1);
}

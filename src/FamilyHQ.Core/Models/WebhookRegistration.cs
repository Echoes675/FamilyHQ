namespace FamilyHQ.Core.Models;

public class WebhookRegistration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CalendarInfoId { get; set; }

    public string ChannelId { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }

    // Navigation properties
    public CalendarInfo CalendarInfo { get; set; } = null!;
}

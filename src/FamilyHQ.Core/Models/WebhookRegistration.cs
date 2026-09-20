namespace FamilyHQ.Core.Models;

public class WebhookRegistration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CalendarInfoId { get; set; }

    public string ChannelId { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string ChannelToken { get; set; } = string.Empty;

    /// <summary>
    /// FHQ-196. Lowercase hex SHA-256 of the full address this channel was registered with
    /// (<c>WebhookAddress.Hash</c>). It is what lets a registration pass notice that
    /// <c>Sync:WebhookBaseUrl</c> has changed and re-register instead of skipping a channel that
    /// still has days left but points at the previous address.
    /// <para>
    /// The address itself is deliberately not stored: a RelayRobin address embeds its route key,
    /// the credential that authorises posting a notification to FamilyHQ, and the only question
    /// asked of this column is equality with the hash of the address configured now.
    /// </para>
    /// <para>
    /// Nullable because every row written before FHQ-196 has no value. A null reads as a mismatch,
    /// so those channels re-register once (new channel, then stop the old one) and fill it in.
    /// </para>
    /// </summary>
    public string? RegisteredAddressHash { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }

    // Navigation properties
    public CalendarInfo CalendarInfo { get; set; } = null!;
}

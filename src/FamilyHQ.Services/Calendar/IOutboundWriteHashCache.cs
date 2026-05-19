namespace FamilyHQ.Services.Calendar;

/// <summary>
/// Records hashes of recent outbound writes to Google Calendar so the sync pipeline
/// can detect and skip webhooks echoing our own writes. Entries expire after a short TTL.
/// </summary>
public interface IOutboundWriteHashCache
{
    void Record(string googleEventId, string contentHash);
    bool WasRecentlyWritten(string googleEventId, string contentHash);
}

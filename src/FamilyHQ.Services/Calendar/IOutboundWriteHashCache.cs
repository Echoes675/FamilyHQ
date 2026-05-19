namespace FamilyHQ.Services.Calendar;

/// <summary>
/// Records hashes of recent outbound writes to Google Calendar so the sync pipeline
/// can detect and skip webhooks echoing our own writes. Entries expire after a 60-second TTL.
/// </summary>
public interface IOutboundWriteHashCache
{
    /// <summary>
    /// Records an outbound write so subsequent webhook echoes can be detected and skipped.
    /// </summary>
    /// <param name="googleEventId">The Google Calendar event ID.</param>
    /// <param name="contentHash">A hash of the event content.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="googleEventId"/> or <paramref name="contentHash"/> is null or empty.</exception>
    void Record(string googleEventId, string contentHash);

    /// <summary>
    /// Returns true when (<paramref name="googleEventId"/>, <paramref name="contentHash"/>) matches a recently-recorded outbound write within the 60-second TTL.
    /// Returns false for null or empty arguments rather than throwing — bad lookups can't corrupt state and we'd rather degrade gracefully than crash a sync loop.
    /// </summary>
    /// <param name="googleEventId">The Google Calendar event ID.</param>
    /// <param name="contentHash">A hash of the event content.</param>
    /// <returns>True if the pair matches a recent write within the TTL; false otherwise.</returns>
    bool WasRecentlyWritten(string googleEventId, string contentHash);
}

namespace FamilyHQ.Core.Exceptions;

/// <summary>
/// FHQ-172. A recurring write needed the series' true origin — the master's DTSTART as Google holds
/// it — and Google did not supply one, so the write was refused and nothing was sent.
/// </summary>
/// <remarks>
/// <para>
/// The refusal is the point. The only other value in scope is the earliest locally-synced row, which
/// is a proxy: when the master predates the sync window it sits later than the true origin, and
/// writing it back relocates the series forward, deleting every occurrence before the window from
/// Google and from every device the calendar is shared with. Under the prime directive an edit may
/// change what the user asked for and nothing else, so a value we know may be wrong is not
/// permitted to reach a Google write at all.
/// </para>
/// <para>
/// <b>Retrying is always safe; whether it can ever succeed depends on a cause this type cannot
/// see.</b> The request left the calendar untouched, so a retry cannot compound anything. But the
/// two shapes behind it differ: a master that was momentarily unreadable becomes readable again and
/// the retry works, whereas a master that has been permanently deleted never resolves and no
/// number of retries will change that — the series has to be edited in the Google Calendar app, or
/// rebuilt. So this is not a "try again in a moment" condition in general, and nothing in the
/// system may schedule an automatic retry on the strength of it. The user-facing messages offer
/// both routes rather than promising the transient one.
/// </para>
/// <para>
/// <b>The message reaches the kiosk (FHQ-175).</b> <see cref="DomainException.UserMessage"/> is
/// rendered in the event modal in place of the generic "please try again", and because the text
/// already offers both the retry and the edit-in-Google routes, the kiosk adds no retry hint of
/// its own. The kiosk caps what it shows at 280 characters, so the two user messages here are
/// written to fit under that; the longer <see cref="Exception.Message"/> goes to the logs and to
/// <c>ProblemDetails.Detail</c> for API clients unchanged.
/// </para>
/// <para>
/// <b>Why 409 and not 400 or 500.</b> The condition is not the caller's fault, so this type does
/// not derive from <see cref="DomainValidationException"/> and does not carry the "Validation
/// Failed" title FHQ-172 recorded as inaccurate. It is not a server fault either: it is a
/// deliberate, handled refusal in which nothing was written, and a 500 would present it as an
/// unhandled fault, page as one, and be counted as one. 409 Conflict is the fit — RFC 9110 §15.5.10
/// reserves it for a request that cannot be applied to the target resource in its current state,
/// "in situations where the user might be able to resolve the conflict and resubmit", which is
/// exactly the shape here: the series' Google-side state is what blocks the write, and the user's
/// routes are a later retry or editing the series where its origin is known. It is also the status
/// FamilyHQ already uses for the other upstream-state precondition that blocks a write with nothing
/// written (reauth required), so a client's 409 handling stays uniform. 422 was rejected because it
/// asserts a defect in the request content, and there is none.
/// </para>
/// </remarks>
public sealed class SeriesOriginUnresolvedException : DomainException
{
    /// <summary>ProblemDetails title. A stable string the HTTP contract may rely on.</summary>
    public const string Title = "Series Origin Unavailable";

    private SeriesOriginUnresolvedException(string message, string userMessage)
        : base(message, userMessage)
    {
    }

    /// <summary>
    /// An "all events" edit changed the series' times, which cannot be re-anchored without the
    /// true origin.
    /// </summary>
    public static SeriesOriginUnresolvedException ForSeriesTimingChange() => new(
        "This series' original start could not be read from Google, so its times cannot be " +
        "changed for the whole series without moving the series itself. Nothing has been " +
        "changed. If the series still exists in Google Calendar this may clear on a retry; " +
        "if it does not, edit the series in the Google Calendar app instead.",
        "This series' original start couldn't be read from Google, so its times can't be changed " +
        "for the whole series. Nothing has been changed. If the series still exists in Google " +
        "Calendar this may clear on a retry; if not, edit the series in the Google Calendar app instead.");

    /// <summary>
    /// A "this and following" split of a COUNT-bounded series needs the true origin to work out how
    /// many occurrences remain.
    /// </summary>
    public static SeriesOriginUnresolvedException ForSeriesSplit() => new(
        "This series' original start could not be read from Google, so the number of " +
        "occurrences left after this one cannot be worked out reliably. Nothing has been " +
        "changed. If the series still exists in Google Calendar this may clear on a retry; " +
        "if it does not, split the series in the Google Calendar app instead.",
        "This series' original start couldn't be read from Google, so the occurrences left after " +
        "this one can't be worked out. Nothing has been changed. If the series still exists in " +
        "Google Calendar this may clear on a retry; if not, split the series in the Google Calendar app instead.");
}

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
/// <b>The message does not reach the kiosk.</b> Stated plainly because the obvious assumption is
/// wrong: <c>CalendarApiService</c> calls <c>EnsureSuccessStatusCode()</c> and discards the response
/// body, and <c>EventModal</c> renders a fixed "Couldn't save your event — please try again."
/// Nothing in <c>FamilyHQ.WebUI</c> reads <c>ProblemDetails.Detail</c> at all. The message therefore
/// reaches the server logs and any direct API client, and nowhere else — so no behaviour may depend
/// on a person having read it. Teaching the kiosk to surface <c>Detail</c> would change the error
/// text of every domain exception at once and is tracked separately.
/// </para>
/// <para>
/// <b>Why 400 and not 500.</b> The condition is not the caller's fault, so 400 is a compromise
/// rather than a fit — stated here rather than hidden. It is still the better of the two: this is a
/// deliberate, handled refusal in which nothing was written, and a 500 would present it as an
/// unhandled server fault, page as one, and be counted as one. No currently-mapped status describes
/// "upstream state could not be established, nothing was written". Revisit if such a mapping is ever
/// added.
/// </para>
/// <para>
/// <b>The ProblemDetails title is inaccurate for this condition.</b> <c>DomainExceptionHandler</c>
/// maps every <see cref="DomainValidationException"/> to the title "Validation Failed", and nothing
/// about this failed validation. Changing that title means changing the shared handler's mapping
/// for a whole exception family, which is a wider contract change than this fix warrants and is out
/// of scope here. It costs nothing today precisely because the kiosk renders neither the title nor
/// the detail; it is recorded so that whoever does surface <c>ProblemDetails</c> fixes the title in
/// the same change.
/// </para>
/// </remarks>
public sealed class SeriesOriginUnresolvedException : DomainValidationException
{
    public SeriesOriginUnresolvedException(string message)
        : base(message)
    {
    }
}

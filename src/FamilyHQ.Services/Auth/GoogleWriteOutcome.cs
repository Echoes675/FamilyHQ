using System.Net;

namespace FamilyHQ.Services.Auth;

/// <summary>
/// FHQ-173. The single answer to one question about a failed Google API call: <b>may Google already
/// have PROCESSED the request?</b>
/// </summary>
/// <remarks>
/// <para>
/// Two call sites turn on that question and must never disagree about it:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <c>ResilientGoogleCalendarClient</c> — a request that was rejected without being processed is
///     safe to send again; one that may have been processed is safe to repeat only when the
///     operation is idempotent, or the retry duplicates a real write.
///   </description></item>
///   <item><description>
///     <c>CalendarEventService.SplitSeriesAsync</c> — a failure that was rejected without being
///     processed can be compensated (undo the write that DID land); one that may have been processed
///     must not be, because "undoing" a write that actually committed destroys the family's data.
///   </description></item>
/// </list>
/// <para>
/// The rule used to live only inside the retry decorator. It is shared rather than restated because
/// two copies would drift and the drift would be silent — one site would keep retrying a shape the
/// other had started treating as safe to undo.
/// </para>
/// <para>
/// <b>The default is "yes, it may have been processed."</b> Only a status code Google itself returned
/// counts as evidence of a rejection: a 4xx means the request reached Google, was understood and was
/// refused, so nothing was written. Everything else — a 5xx, a timeout, a dropped connection, a
/// cancellation, an exception shape not seen before — is ambiguous, and ambiguity resolves to the
/// answer whose wrong case is recoverable.
/// </para>
/// </remarks>
public static class GoogleWriteOutcome
{
    /// <summary>
    /// True when <paramref name="failure"/> leaves it possible that Google applied the request
    /// anyway; false only on positive evidence that Google rejected it without processing it.
    /// </summary>
    public static bool MayHaveBeenProcessed(Exception failure) => failure switch
    {
        // The credentials were refused, so the call never got past authorisation. Checked before
        // GoogleApiException in case the two are ever related by inheritance.
        GoogleReauthRequiredException => false,

        // Google answered. 4xx = understood and refused, nothing written. 5xx = Google's own side
        // failed, and it may have failed AFTER applying the change — that is exactly why the retry
        // decorator refuses to repeat a 5xx for a create.
        GoogleApiException api => (int)api.StatusCode >= (int)HttpStatusCode.InternalServerError,

        // No answer from Google at all: a per-attempt HttpClient timeout (FHQ-91), a socket error, a
        // cancelled request, or something unclassified. The request may have been fully processed
        // with only the response lost.
        _ => true
    };
}

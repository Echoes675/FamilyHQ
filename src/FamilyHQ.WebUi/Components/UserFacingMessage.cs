using FamilyHQ.WebUi.Services;

namespace FamilyHQ.WebUi.Components;

/// <summary>
/// The one shape every server-supplied string passes through before it reaches the kiosk screen:
/// blank → the component's own fallback; otherwise trimmed and capped at <see cref="MaxLength"/>
/// with an ellipsis. Extracted from the reauth banner (FHQ-175) so the event modal and the chip
/// selector share it rather than re-deriving it.
/// </summary>
public static class UserFacingMessage
{
    /// <summary>
    /// Upper bound on what a 7" portrait screen will show of one message. Server strings meant for
    /// the kiosk are written to fit under it; the cap exists for the ones that were not.
    /// </summary>
    public const int MaxLength = 280;

    private const string Ellipsis = "…";

    /// <summary>Caps a message, or substitutes <paramref name="fallback"/> when it is blank.</summary>
    public static string Format(string? message, string fallback)
    {
        if (string.IsNullOrWhiteSpace(message))
            return fallback;

        var trimmed = message.Trim();
        return trimmed.Length <= MaxLength
            ? trimmed
            : trimmed[..MaxLength].TrimEnd() + Ellipsis;
    }

    /// <summary>
    /// What a catch site should show for a failed calendar write. A <see cref="CalendarApiException"/>
    /// carrying a server-vetted <see cref="CalendarApiException.UserMessage"/> is rendered (capped);
    /// anything else — a transport failure, a response with no vetted text — gets the component's
    /// generic <paramref name="fallback"/>. The fallback is the only place "please try again" may
    /// appear: a vetted message carries its own advice, and for a terminal failure a retry hint
    /// would be exactly wrong.
    /// </summary>
    public static string ForFailure(Exception exception, string fallback) =>
        exception is CalendarApiException { UserMessage: { } userMessage }
            ? Format(userMessage, fallback)
            : fallback;
}

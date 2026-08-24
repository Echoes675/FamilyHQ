using FamilyHQ.Core.DTOs;

namespace FamilyHQ.WebUi.Components.Reauth;

public static class ReauthBanner
{
    public const string NeedsReauthStatus = "needs_reauth";
    public const int MaxErrorLength = UserFacingMessage.MaxLength;
    public const string DefaultMessage = "Your sync needs you to sign in again to refresh access.";

    public static bool ShouldShow(ConnectionStatusDto? status) =>
        status is not null
        && string.Equals(status.Status, NeedsReauthStatus, StringComparison.OrdinalIgnoreCase);

    public static string FormatMessage(string? lastError) =>
        UserFacingMessage.Format(lastError, DefaultMessage);
}

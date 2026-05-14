using FamilyHQ.Core.DTOs;

namespace FamilyHQ.WebUi.Components.Diagnostics;

public static class DiagnosticsView
{
    public const string NeedsReauthStatus = "needs_reauth";
    public const string ActiveStatus = "active";

    public static bool ShouldShowEmptyState(IReadOnlyList<SyncFailureDto>? failures) =>
        failures is null || failures.Count == 0;

    public static bool IsNeedsReauth(ConnectionStatusWithCalendarsDto? status) =>
        status is not null
        && string.Equals(status.Status, NeedsReauthStatus, StringComparison.OrdinalIgnoreCase);

    public static string StatusBadgeLabel(ConnectionStatusWithCalendarsDto? status)
    {
        if (status is null) return "Unknown";
        return IsNeedsReauth(status) ? "Needs Reauth" : "Active";
    }

    public static string StatusBadgeCssClass(ConnectionStatusWithCalendarsDto? status)
    {
        if (status is null) return "diagnostics-status-badge diagnostics-status-badge--unknown";
        return IsNeedsReauth(status)
            ? "diagnostics-status-badge diagnostics-status-badge--warning"
            : "diagnostics-status-badge diagnostics-status-badge--ok";
    }

    public static string FormatRelative(DateTimeOffset when, DateTimeOffset now)
    {
        var delta = now - when;

        if (delta.TotalSeconds < 0)
            return "just now";

        if (delta.TotalSeconds < 45)
            return "just now";

        if (delta.TotalMinutes < 60)
        {
            var minutes = (int)Math.Round(delta.TotalMinutes);
            if (minutes <= 0) minutes = 1;
            return $"{minutes}m ago";
        }

        if (delta.TotalHours < 24)
        {
            var hours = (int)Math.Floor(delta.TotalHours);
            return $"{hours}h ago";
        }

        if (delta.TotalDays < 30)
        {
            var days = (int)Math.Floor(delta.TotalDays);
            return $"{days}d ago";
        }

        return when.ToString("yyyy-MM-dd");
    }

    public static string FormatAbsolute(DateTimeOffset when) =>
        when.ToString("yyyy-MM-dd HH:mm:ss zzz");
}

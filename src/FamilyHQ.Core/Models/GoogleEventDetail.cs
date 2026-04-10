namespace FamilyHQ.Core.Models;

/// <summary>
/// Lightweight result of IGoogleCalendarClient.GetEventAsync.
/// Used by the webhook handler to detect self-generated echo events.
/// </summary>
public record GoogleEventDetail(
    string Id,
    string? OrganizerEmail,
    string? ContentHash);

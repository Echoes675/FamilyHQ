namespace FamilyHQ.Core.Models;

/// <summary>
/// Lightweight result of IGoogleCalendarClient.GetEventAsync.
/// Used only by the service layer to perform the external-attendee check before delete.
/// Blazor components must not use this type.
/// </summary>
public record GoogleEventDetail(
    string Id,
    string? OrganizerEmail,
    IReadOnlyList<string> AttendeeEmails);

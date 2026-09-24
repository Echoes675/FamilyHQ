namespace FamilyHQ.Smoke.Data.Models;

/// <summary>A calendar as preprod's <c>GET /api/calendars</c> describes it.</summary>
public sealed record PreprodCalendar(
    Guid Id, string DisplayName, string? Color, bool IsShared, bool IsVisible);

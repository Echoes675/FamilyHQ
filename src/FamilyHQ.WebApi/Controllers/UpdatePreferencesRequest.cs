namespace FamilyHQ.WebApi.Controllers;

public record UpdatePreferencesRequest(
    int EventDensity,
    string? CalendarColumnOrder,
    string? CalendarColorOverrides
);
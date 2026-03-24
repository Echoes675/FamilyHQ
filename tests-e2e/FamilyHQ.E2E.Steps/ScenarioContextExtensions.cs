using Reqnroll;

namespace FamilyHQ.E2E.Steps;

public static class ScenarioContextExtensions
{
    public static string GetCurrentCalendarId(this ScenarioContext context)
    {
        if (!context.TryGetValue<string>("CurrentCalendarId", out var calendarId))
            throw new InvalidOperationException(
                "No active calendar has been selected. " +
                "Add 'And the \"<calendar name>\" calendar is the active calendar' to your scenario.");
        return calendarId;
    }
}

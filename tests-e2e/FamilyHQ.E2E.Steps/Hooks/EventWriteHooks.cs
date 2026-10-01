using FamilyHQ.E2E.Common.Helpers;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps.Hooks;

/// <summary>
/// Records the event-API writes the kiosk makes, for scenarios that assert what was SENT rather than
/// what the screen shows afterwards.
/// </summary>
/// <remarks>
/// Attached before the scenario's first step so a write made by a Given is recorded too, and scoped
/// to the tagged feature so no other scenario pays for a listener it does not read. The recorder is
/// bound to this scenario's own page and dies with it, so nothing leaks between scenarios under the
/// parallel runner.
/// </remarks>
[Binding]
public class EventWriteHooks
{
    /// <summary>The scenario-context key the recorder is stored under.</summary>
    public const string RecorderKey = "EventWriteRecorder";

    private readonly ScenarioContext _scenarioContext;

    public EventWriteHooks(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [BeforeScenario("EventReminders", Order = 3)]
    public void RecordEventWrites()
    {
        var page = _scenarioContext.Get<IPage>();
        _scenarioContext.Set(EventWriteRecorder.AttachTo(page), RecorderKey);
    }
}

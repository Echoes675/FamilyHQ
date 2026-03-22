using Microsoft.Playwright;
using Reqnroll;
using Xunit.Abstractions;
using FamilyHQ.E2E.Data.Api;

namespace FamilyHQ.E2E.Steps.Hooks;

[Binding]
public class CorrelationIdHooks
{
    private readonly ScenarioContext _scenarioContext;
    private readonly SimulatorApiClient _simulatorApi;
    private readonly ITestOutputHelper _testOutputHelper;

    public CorrelationIdHooks(ScenarioContext scenarioContext, SimulatorApiClient simulatorApi, ITestOutputHelper testOutputHelper)
    {
        _scenarioContext = scenarioContext;
        _simulatorApi = simulatorApi;
        _testOutputHelper = testOutputHelper;
    }

    [BeforeScenario(Order = 2)]
    public async Task SetCorrelationIdAsync()
    {
        var testCorrelationId = Guid.NewGuid().ToString();

        _testOutputHelper.WriteLine($"Starting Test '{_scenarioContext.ScenarioInfo.Title}' with TestCorrelationId: {testCorrelationId}");
        
        _scenarioContext.Set(testCorrelationId, "TestCorrelationId");
        _simulatorApi.SetCorrelationId(testCorrelationId);

        // Inject into Playwright Page if it exists
        if (_scenarioContext.TryGetValue(out IPage page))
        {
            // This runs before page scripts execute on every navigation in this context
            await page.AddInitScriptAsync(@$"
                localStorage.setItem('familyhq_session_correlation_id', '{testCorrelationId}');
            ");
        }
    }

    [AfterScenario(Order = 2)]
    public async Task ClearCorrelationIdAsync()
    {
        // Enforce isolation by purging the correlation id
        if (_scenarioContext.TryGetValue(out IPage page))
        {
            try
            {
                await page.EvaluateAsync("localStorage.removeItem('familyhq_session_correlation_id');");
            }
            catch
            {
                // Page might be closed or navigation failed, safe to ignore during teardown
            }
        }
    }
}

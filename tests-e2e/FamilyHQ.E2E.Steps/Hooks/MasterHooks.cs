using FamilyHQ.E2E.Common.Configuration;
using FamilyHQ.E2E.Common.Hooks;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps.Hooks;

[Binding]
public class MasterHooks
{
    private readonly PlaywrightDriver _driver;
    private readonly ScenarioContext _scenarioContext;
    private readonly TestConfiguration _config;

    public MasterHooks(PlaywrightDriver driver, ScenarioContext scenarioContext)
    {
        _driver = driver;
        _scenarioContext = scenarioContext;
        _config = ConfigurationLoader.Load();
    }

    [BeforeScenario(Order = 1)]
    public async Task SetupBrowserAsync()
    {
        var page = await _driver.InitializeAsync(_config);
        
        // Make the page accessible to Step Definitions via context DI
        _scenarioContext.Set(page);
    }

    [AfterScenario(Order = 1)]
    public async Task TeardownBrowserAsync()
    {
        await _driver.DisposeAsync();
    }
}

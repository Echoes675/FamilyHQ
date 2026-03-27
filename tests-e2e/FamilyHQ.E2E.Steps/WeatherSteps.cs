using System.Net.Http.Json;
using FamilyHQ.E2E.Common.Configuration;
using FamilyHQ.E2E.Data.Api;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

[Binding]
public class WeatherSteps
{
    private readonly SimulatorApiClient _simulatorClient;

    public WeatherSteps()
    {
        _simulatorClient = new SimulatorApiClient();
    }

    [When(@"the simulator sets weather to ""([^""]*)""")]
    public async Task WhenTheSimulatorSetsWeatherToAsync(string weatherCondition)
    {
        var config = ConfigurationLoader.Load();
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(config.SimulatorApiUrl)
        };

        var body = new
        {
            Condition = weatherCondition,
            Temperature = 20
        };

        var response = await httpClient.PostAsJsonAsync("api/simulator/weather", body);
        response.EnsureSuccessStatusCode();
    }
}

namespace FamilyHQ.E2E.Common.Configuration;

using Microsoft.Extensions.Configuration;

public static class ConfigurationLoader
{
    public static TestConfiguration Load()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        var testConfig = new TestConfiguration();
        config.GetSection("TestConfiguration").Bind(testConfig);
        return testConfig;
    }
}
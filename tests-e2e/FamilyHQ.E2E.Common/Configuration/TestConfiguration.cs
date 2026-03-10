namespace FamilyHQ.E2E.Common.Configuration;

public class TestConfiguration
{
    public string BaseUrl { get; set; } = "https://localhost:7154";
    public string SimulatorApiUrl { get; set; } = "https://localhost:7199";
    public bool Headless { get; set; } = true;
    public int DefaultTimeoutMs { get; set; } = 30000;
}
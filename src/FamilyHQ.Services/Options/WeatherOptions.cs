namespace FamilyHQ.Services.Options;

public class WeatherOptions
{
    public const string SectionName = "Weather";

    public string BaseUrl { get; set; } = "https://api.open-meteo.com";
    public int PollIntervalMinutes { get; set; } = 30;
    public int MinPollIntervalMinutes { get; set; } = 1;
    public double WindThresholdKmh { get; set; } = 30;
    public bool Enabled { get; set; } = true;
}

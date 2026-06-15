namespace FamilyHQ.Services.Options;

public class GoogleCalendarOptions
{
    public const string SectionName = "GoogleCalendar";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string AuthPromptUrl { get; set; } = string.Empty;
    public string AuthBaseUrl { get; set; } = string.Empty;
    public string CalendarApiBaseUrl { get; set; } = string.Empty;
    public string JwksUri { get; set; } = "https://www.googleapis.com/oauth2/v3/certs";
    public string ValidIssuer { get; set; } = "https://accounts.google.com";
}

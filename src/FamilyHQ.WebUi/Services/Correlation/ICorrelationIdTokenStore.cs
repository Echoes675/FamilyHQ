namespace FamilyHQ.WebUi.Services.Correlation;

public interface ICorrelationIdTokenStore
{
    Task<string?> GetSessionCorrelationIdAsync();
    Task SetSessionCorrelationIdAsync(string correlationId);
    Task ClearSessionCorrelationIdAsync();
}

namespace FamilyHQ.WebUi.Services.Auth;

public interface IAuthTokenStore
{
    Task<string?> GetTokenAsync();
    Task SetTokenAsync(string token);
    Task ClearTokenAsync();
}

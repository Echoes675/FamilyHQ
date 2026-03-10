using Microsoft.JSInterop;

namespace FamilyHQ.WebUi.Services.Auth;

public class LocalStorageAuthTokenStore : IAuthTokenStore
{
    private readonly IJSRuntime _jsRuntime;
    private const string TokenKey = "familyhq_auth_token";

    public LocalStorageAuthTokenStore(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task<string?> GetTokenAsync()
    {
        return await _jsRuntime.InvokeAsync<string>("localStorage.getItem", TokenKey);
    }

    public async Task SetTokenAsync(string token)
    {
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", TokenKey, token);
    }

    public async Task ClearTokenAsync()
    {
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", TokenKey);
    }
}

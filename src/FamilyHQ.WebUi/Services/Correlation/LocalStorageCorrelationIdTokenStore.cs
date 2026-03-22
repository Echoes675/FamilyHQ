using Microsoft.JSInterop;

namespace FamilyHQ.WebUi.Services.Correlation;

public class LocalStorageCorrelationIdTokenStore : ICorrelationIdTokenStore
{
    private readonly IJSRuntime _jsRuntime;
    private const string TokenKey = "familyhq_session_correlation_id";

    public LocalStorageCorrelationIdTokenStore(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task<string?> GetSessionCorrelationIdAsync()
    {
        return await _jsRuntime.InvokeAsync<string>("localStorage.getItem", TokenKey);
    }

    public async Task SetSessionCorrelationIdAsync(string correlationId)
    {
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", TokenKey, correlationId);
    }

    public async Task ClearSessionCorrelationIdAsync()
    {
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", TokenKey);
    }
}

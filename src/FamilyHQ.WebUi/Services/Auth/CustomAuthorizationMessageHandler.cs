using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;

namespace FamilyHQ.WebUi.Services.Auth;

public class CustomAuthorizationMessageHandler : DelegatingHandler
{
    private readonly IAuthTokenStore _tokenStore;
    private readonly NavigationManager _navigationManager;
    private readonly ILogger<CustomAuthorizationMessageHandler> _logger;

    public CustomAuthorizationMessageHandler(IAuthTokenStore tokenStore, NavigationManager navigationManager, ILogger<CustomAuthorizationMessageHandler> logger)
    {
        _tokenStore = tokenStore;
        _navigationManager = navigationManager;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokenStore.GetTokenAsync();
        var hadToken = !string.IsNullOrEmpty(token);
        if (hadToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (hadToken && response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Bearer token rejected (401); clearing token and redirecting to login");
            await _tokenStore.ClearTokenAsync();
            _navigationManager.NavigateTo("/", forceLoad: true);
        }

        return response;
    }
}

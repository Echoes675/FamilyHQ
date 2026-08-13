using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;

namespace FamilyHQ.WebUi.Services.Auth;

public class CustomAuthorizationMessageHandler : DelegatingHandler
{
    private readonly IAuthTokenStore _tokenStore;
    private readonly IJwtRenewalService _renewalService;
    private readonly NavigationManager _navigationManager;
    private readonly ILogger<CustomAuthorizationMessageHandler> _logger;

    public CustomAuthorizationMessageHandler(
        IAuthTokenStore tokenStore,
        IJwtRenewalService renewalService,
        NavigationManager navigationManager,
        ILogger<CustomAuthorizationMessageHandler> logger)
    {
        _tokenStore = tokenStore;
        _renewalService = renewalService;
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
            // FHQ-126: before signing the kiosk out, attempt ONE silent renewal and retry the
            // original request once. The renewal goes through the handler-free "Auth" client, so
            // it cannot re-enter this handler, and this block is structurally single-shot — the
            // retried response falls through to the clear+redirect below if it is still 401.
            var renewedToken = await _renewalService.RenewNowAsync(cancellationToken);
            if (!string.IsNullOrEmpty(renewedToken))
            {
                _logger.LogInformation("Bearer token rejected (401); renewed JWT and retrying the request once.");
                response.Dispose();
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", renewedToken);
                response = await base.SendAsync(request, cancellationToken);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("Bearer token rejected (401); clearing token and redirecting to login");
                await _tokenStore.ClearTokenAsync();
                _navigationManager.NavigateTo("/", forceLoad: true);
            }
        }

        return response;
    }
}

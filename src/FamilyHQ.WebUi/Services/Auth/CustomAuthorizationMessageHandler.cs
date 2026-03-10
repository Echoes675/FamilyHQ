using System.Net.Http.Headers;

namespace FamilyHQ.WebUi.Services.Auth;

public class CustomAuthorizationMessageHandler : DelegatingHandler
{
    private readonly IAuthTokenStore _tokenStore;

    public CustomAuthorizationMessageHandler(IAuthTokenStore tokenStore)
    {
        _tokenStore = tokenStore;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokenStore.GetTokenAsync();
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}

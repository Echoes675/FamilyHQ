using System.Net.Http.Headers;
using FamilyHQ.Core.Constants;

namespace FamilyHQ.WebUi.Services.Correlation;

public class CorrelationIdMessageHandler : DelegatingHandler
{
    private readonly ICorrelationIdTokenStore _tokenStore;

    public CorrelationIdMessageHandler(ICorrelationIdTokenStore tokenStore)
    {
        _tokenStore = tokenStore;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString();
        request.Headers.Add(CorrelationConstants.CorrelationIdHeaderName, correlationId);

        var sessionCorrelationId = await _tokenStore.GetSessionCorrelationIdAsync();
        if (!string.IsNullOrEmpty(sessionCorrelationId))
        {
            request.Headers.Add(CorrelationConstants.SessionCorrelationIdHeaderName, sessionCorrelationId);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}

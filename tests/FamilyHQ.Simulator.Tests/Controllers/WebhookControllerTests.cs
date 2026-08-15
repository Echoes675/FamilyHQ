using System.Net;
using FamilyHQ.Simulator.Controllers;
using FamilyHQ.Simulator.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Moq.Protected;
using Xunit;

namespace FamilyHQ.Simulator.Tests.Controllers;

// FHQ-101: the WebApi acks every legitimate push with a 2xx, so a non-2xx (a 429 from the webhook
// rate limiter) means the push never landed. The Simulator used to report OK regardless, which
// turned a rejected push into an unrelated "event never appeared" timeout 30s later in whichever
// E2E scenario happened to be running. These pin the propagation.
public class WebhookControllerTests
{
    [Fact]
    public async Task PushWebhook_WhenWebApiAcks_ReturnsOk()
    {
        // Arrange
        var sut = CreateSut(HttpStatusCode.OK);

        // Act
        var result = await sut.PushWebhook();

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task PushWebhook_WhenWebApiRateLimits_PropagatesThe429()
    {
        // Arrange
        var sut = CreateSut(HttpStatusCode.TooManyRequests);

        // Act
        var result = await sut.PushWebhook();

        // Assert — the push step itself must fail, loudly and with the real status.
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be((int)HttpStatusCode.TooManyRequests);
        objectResult.Value.Should().BeOfType<string>()
            .Which.Should().Contain(nameof(HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public async Task PushWebhook_WhenWebApiFails_PropagatesTheFailureStatus()
    {
        // Arrange
        var sut = CreateSut(HttpStatusCode.InternalServerError);

        // Act
        var result = await sut.PushWebhook();

        // Assert
        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
    }

    private static WebhookController CreateSut(HttpStatusCode webApiStatus)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(webApiStatus));

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(handler.Object));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WebApiBaseUrl"] = "https://webapi.test"
            })
            .Build();

        return new WebhookController(configuration, CreateDb(), factory.Object);
    }

    private static SimContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SimContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SimContext(options);
    }
}

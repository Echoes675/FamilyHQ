using FamilyHQ.Core.Interfaces;
using FamilyHQ.WebApi.Controllers;
using FamilyHQ.WebApi.Hubs;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.WebApi.Tests.Controllers;

public class SyncControllerTests
{
    [Fact]
    public async Task TriggerSync_ShouldCallSyncAndNotifyClients()
    {
        // Arrange
        var (sync, proxy, systemUnderTest) = CreateSut();

        // Act
        var result = await systemUnderTest.TriggerSync(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        
        sync.Verify(s => s.SyncAllAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        proxy.Verify(c => c.SendCoreAsync("EventsUpdated", Array.Empty<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GooglePushWebhook_ShouldAcknowledgeGoogleThenSyncAndNotify()
    {
        // Arrange
        var (sync, proxy, systemUnderTest) = CreateSut();
        systemUnderTest.ControllerContext.HttpContext.Request.Headers.Append("x-goog-resource-state", "sync");

        // Act
        var result = await systemUnderTest.GooglePushWebhook(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkResult>();
        
        sync.Verify(s => s.SyncAllAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        proxy.Verify(c => c.SendCoreAsync("EventsUpdated", Array.Empty<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (Mock<ICalendarSyncService> Sync, Mock<IClientProxy> Proxy, SyncController SystemUnderTest) CreateSut()
    {
        var syncServiceMock = new Mock<ICalendarSyncService>();
        var hubContextMock = new Mock<IHubContext<CalendarHub>>();
        var clientProxyMock = new Mock<IClientProxy>();
        var loggerMock = new Mock<ILogger<SyncController>>();

        var clientsMock = new Mock<IHubClients>();
        clientsMock.Setup(c => c.All).Returns(clientProxyMock.Object);
        hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        var systemUnderTest = new SyncController(
            syncServiceMock.Object,
            hubContextMock.Object,
            loggerMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        return (syncServiceMock, clientProxyMock, systemUnderTest);
    }
}

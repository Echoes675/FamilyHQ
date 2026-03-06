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
    private readonly Mock<ICalendarSyncService> _syncServiceMock;
    private readonly Mock<IHubContext<CalendarHub>> _hubContextMock;
    private readonly Mock<IClientProxy> _clientProxyMock;
    private readonly Mock<ILogger<SyncController>> _loggerMock;
    private readonly SyncController _sut;

    public SyncControllerTests()
    {
        _syncServiceMock = new Mock<ICalendarSyncService>();
        _hubContextMock = new Mock<IHubContext<CalendarHub>>();
        _clientProxyMock = new Mock<IClientProxy>();
        _loggerMock = new Mock<ILogger<SyncController>>();

        // Setup SignalR HubContext mock
        var clientsMock = new Mock<IHubClients>();
        clientsMock.Setup(c => c.All).Returns(_clientProxyMock.Object);
        _hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        _sut = new SyncController(
            _syncServiceMock.Object,
            _hubContextMock.Object,
            _loggerMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    [Fact]
    public async Task TriggerSync_ShouldCallSyncAndNotifyClients()
    {
        // Act
        var result = await _sut.TriggerSync(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        
        _syncServiceMock.Verify(s => s.SyncAllAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        
        _clientProxyMock.Verify(c => c.SendCoreAsync("EventsUpdated", Array.Empty<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GooglePushWebhook_ShouldAcknowledgeGoogleThenSyncAndNotify()
    {
        // Arrange
        _sut.ControllerContext.HttpContext.Request.Headers.Append("x-goog-resource-state", "sync");

        // Act
        var result = await _sut.GooglePushWebhook(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkResult>();
        
        _syncServiceMock.Verify(s => s.SyncAllAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        
        _clientProxyMock.Verify(c => c.SendCoreAsync("EventsUpdated", Array.Empty<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}

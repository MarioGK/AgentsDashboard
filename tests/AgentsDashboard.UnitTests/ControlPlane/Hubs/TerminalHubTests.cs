using AgentsDashboard.ControlPlane.Hubs;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace AgentsDashboard.UnitTests.ControlPlane.Hubs;

public class TerminalHubTests
{
    private readonly Mock<ITerminalBridgeService> _mockBridge;
    private readonly Mock<ILogger<TerminalHub>> _mockLogger;

    public TerminalHubTests()
    {
        _mockBridge = new Mock<ITerminalBridgeService>();
        _mockLogger = new Mock<ILogger<TerminalHub>>();
    }

    private TerminalHub CreateHub(string connectionId = "conn-1")
    {
        var hub = new TerminalHub(_mockBridge.Object, _mockLogger.Object);
        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(x => x.ConnectionId).Returns(connectionId);
        mockContext.Setup(x => x.ConnectionAborted).Returns(CancellationToken.None);
        var mockClients = new Mock<IHubCallerClients>();
        var mockGroups = new Mock<IGroupManager>();
        hub.Context = mockContext.Object;
        hub.Clients = mockClients.Object;
        hub.Groups = mockGroups.Object;
        return hub;
    }

    [Test]
    public void TerminalHub_InheritsFromHub()
    {
        typeof(TerminalHub).Should().BeAssignableTo<Hub>();
    }

    [Test]
    public void TerminalHub_IsSealed()
    {
        typeof(TerminalHub).IsSealed.Should().BeTrue();
    }

    [Test]
    public void TerminalHub_HasAuthorizeAttribute()
    {
        var attr = typeof(TerminalHub).GetCustomAttributes(typeof(AuthorizeAttribute), true);
        attr.Should().NotBeEmpty();
    }

    [Test]
    public async Task CreateSession_DelegatesToBridgeService()
    {
        var hub = CreateHub("conn-create");
        _mockBridge
            .Setup(b => b.CreateSessionAsync("conn-create", "worker-1", null, 80, 24, It.IsAny<CancellationToken>()))
            .ReturnsAsync("session-abc");

        var result = await hub.CreateSession("worker-1", null, 80, 24);

        result.Should().Be("session-abc");
        _mockBridge.Verify(b => b.CreateSessionAsync(
            "conn-create", "worker-1", null, 80, 24, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task CreateSession_WithRunId_PassesRunIdToBridge()
    {
        var hub = CreateHub("conn-run");
        _mockBridge
            .Setup(b => b.CreateSessionAsync("conn-run", "worker-1", "run-123", 120, 30, It.IsAny<CancellationToken>()))
            .ReturnsAsync("session-xyz");

        var result = await hub.CreateSession("worker-1", "run-123", 120, 30);

        result.Should().Be("session-xyz");
        _mockBridge.Verify(b => b.CreateSessionAsync(
            "conn-run", "worker-1", "run-123", 120, 30, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task CreateSession_WhenBridgeThrows_PropagatesException()
    {
        var hub = CreateHub();
        _mockBridge
            .Setup(b => b.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("No workers available"));

        var act = () => hub.CreateSession("worker-1", null, 80, 24);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("No workers available");
    }

    [Test]
    public async Task AttachSession_DelegatesToBridgeService()
    {
        var hub = CreateHub("conn-attach");

        await hub.AttachSession("session-1", 42);

        _mockBridge.Verify(b => b.AttachSessionAsync(
            "conn-attach", "session-1", 42, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Input_DelegatesToBridgeService()
    {
        var hub = CreateHub();

        await hub.Input("session-1", "aGVsbG8=");

        _mockBridge.Verify(b => b.SendInputAsync(
            "session-1", "aGVsbG8=", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Resize_DelegatesToBridgeService()
    {
        var hub = CreateHub();

        await hub.Resize("session-1", 120, 40);

        _mockBridge.Verify(b => b.ResizeAsync(
            "session-1", 120, 40, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Close_DelegatesToBridgeService()
    {
        var hub = CreateHub();

        await hub.Close("session-1");

        _mockBridge.Verify(b => b.CloseAsync(
            "session-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task OnDisconnectedAsync_CallsBridgeCleanup()
    {
        var hub = CreateHub("conn-disconnect");

        await hub.OnDisconnectedAsync(null);

        _mockBridge.Verify(b => b.OnClientDisconnectedAsync("conn-disconnect"), Times.Once);
    }

    [Test]
    public async Task OnDisconnectedAsync_WithException_StillCallsCleanup()
    {
        var hub = CreateHub("conn-err");
        var ex = new InvalidOperationException("Connection lost");

        await hub.OnDisconnectedAsync(ex);

        _mockBridge.Verify(b => b.OnClientDisconnectedAsync("conn-err"), Times.Once);
    }

    [Test]
    public async Task OnDisconnectedAsync_WhenBridgeThrows_DoesNotPropagate()
    {
        var hub = CreateHub("conn-fail");
        _mockBridge
            .Setup(b => b.OnClientDisconnectedAsync("conn-fail"))
            .ThrowsAsync(new Exception("Cleanup failed"));

        var act = () => hub.OnDisconnectedAsync(null);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public void Hub_CanBeInstantiated()
    {
        var hub = CreateHub();
        hub.Should().NotBeNull();
    }

    [Test]
    public async Task Input_WhenBridgeThrows_PropagatesException()
    {
        var hub = CreateHub();
        _mockBridge
            .Setup(b => b.SendInputAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Session not found"));

        var act = () => hub.Input("bad-session", "data");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task Resize_WhenBridgeThrows_PropagatesException()
    {
        var hub = CreateHub();
        _mockBridge
            .Setup(b => b.ResizeAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Session not found"));

        var act = () => hub.Resize("bad-session", 80, 24);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task Close_WhenBridgeThrows_PropagatesException()
    {
        var hub = CreateHub();
        _mockBridge
            .Setup(b => b.CloseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Session not found"));

        var act = () => hub.Close("bad-session");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task CreateSession_PassesConnectionAbortedToken()
    {
        using var cts = new CancellationTokenSource();
        var hub = new TerminalHub(_mockBridge.Object, _mockLogger.Object);
        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(x => x.ConnectionId).Returns("conn-token");
        mockContext.Setup(x => x.ConnectionAborted).Returns(cts.Token);
        hub.Context = mockContext.Object;

        _mockBridge
            .Setup(b => b.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), cts.Token))
            .ReturnsAsync("session-token");

        await hub.CreateSession("worker-1", null, 80, 24);

        _mockBridge.Verify(b => b.CreateSessionAsync(
            "conn-token", "worker-1", null, 80, 24, cts.Token), Times.Once);
    }
}

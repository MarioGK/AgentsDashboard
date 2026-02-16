using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Hubs;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class TerminalBridgeServiceTests
{
    private readonly Mock<IMagicOnionClientFactory> _mockClientFactory;
    private readonly Mock<IOrchestratorStore> _mockStore;
    private readonly Mock<IHubContext<TerminalHub>> _mockHubContext;
    private readonly Mock<ILogger<TerminalBridgeService>> _mockLogger;
    private readonly Mock<ITerminalHub> _mockTerminalHub;
    private readonly Mock<IHubClients> _mockClients;
    private readonly Mock<IClientProxy> _mockClientProxy;
    private readonly TerminalBridgeService _service;

    public TerminalBridgeServiceTests()
    {
        _mockClientFactory = new Mock<IMagicOnionClientFactory>();
        _mockStore = new Mock<IOrchestratorStore>();
        _mockHubContext = new Mock<IHubContext<TerminalHub>>();
        _mockLogger = new Mock<ILogger<TerminalBridgeService>>();
        _mockTerminalHub = new Mock<ITerminalHub>();
        _mockClients = new Mock<IHubClients>();
        _mockClientProxy = new Mock<IClientProxy>();

        _mockHubContext.Setup(h => h.Clients).Returns(_mockClients.Object);
        _mockClients.Setup(c => c.Client(It.IsAny<string>())).Returns(_mockClientProxy.Object);

        _mockClientFactory
            .Setup(f => f.ConnectTerminalHubAsync(It.IsAny<ITerminalReceiver>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockTerminalHub.Object);

        _mockStore
            .Setup(s => s.CreateTerminalSessionAsync(It.IsAny<TerminalSessionDocument>(), It.IsAny<CancellationToken>()))
            .Returns<TerminalSessionDocument, CancellationToken>((session, _) => Task.FromResult(session));

        var options = Options.Create(new TerminalOptions
        {
            ReplayBufferEvents = 2000,
            ResumeGraceMinutes = 10
        });

        _service = new TerminalBridgeService(
            _mockClientFactory.Object,
            _mockStore.Object,
            _mockHubContext.Object,
            options,
            _mockLogger.Object);
    }

    [Test]
    public void Service_ImplementsITerminalBridgeService()
    {
        typeof(TerminalBridgeService).Should().BeAssignableTo<ITerminalBridgeService>();
    }

    [Test]
    public void Service_ImplementsITerminalReceiver()
    {
        typeof(TerminalBridgeService).Should().BeAssignableTo<ITerminalReceiver>();
    }

    [Test]
    public void Service_IsSealed()
    {
        typeof(TerminalBridgeService).IsSealed.Should().BeTrue();
    }

    [Test]
    public async Task CreateSession_CreatesDbRecord()
    {
        var sessionId = await _service.CreateSessionAsync("conn-1", "worker-1", null, 80, 24);

        sessionId.Should().NotBeNullOrEmpty();
        _mockStore.Verify(s => s.CreateTerminalSessionAsync(
            It.Is<TerminalSessionDocument>(doc =>
                doc.WorkerId == "worker-1" &&
                doc.State == TerminalSessionState.Pending &&
                doc.Cols == 80 &&
                doc.Rows == 24),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task CreateSession_ConnectsToWorkerHub()
    {
        await _service.CreateSessionAsync("conn-1", "worker-1", null, 80, 24);

        _mockClientFactory.Verify(
            f => f.ConnectTerminalHubAsync(It.IsAny<ITerminalReceiver>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task CreateSession_SendsOpenSessionToWorker()
    {
        await _service.CreateSessionAsync("conn-1", "worker-1", "run-42", 120, 30);

        _mockTerminalHub.Verify(h => h.OpenSessionAsync(
            It.Is<OpenTerminalSessionRequest>(r =>
                r.WorkerId == "worker-1" &&
                r.RunId == "run-42" &&
                r.Cols == 120 &&
                r.Rows == 30)), Times.Once);
    }

    [Test]
    public async Task CreateSession_WithRunId_PersistsRunId()
    {
        await _service.CreateSessionAsync("conn-1", "worker-1", "run-99", 80, 24);

        _mockStore.Verify(s => s.CreateTerminalSessionAsync(
            It.Is<TerminalSessionDocument>(doc => doc.RunId == "run-99"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task CreateSession_WithoutRunId_PersistsNullRunId()
    {
        await _service.CreateSessionAsync("conn-1", "worker-1", null, 80, 24);

        _mockStore.Verify(s => s.CreateTerminalSessionAsync(
            It.Is<TerminalSessionDocument>(doc => doc.RunId == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task AttachSession_FetchesSessionFromStore()
    {
        // First create a session
        var sessionId = await _service.CreateSessionAsync("conn-1", "worker-1", null, 80, 24);

        _mockStore
            .Setup(s => s.GetTerminalSessionAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TerminalSessionDocument
            {
                Id = sessionId,
                WorkerId = "worker-1",
                State = TerminalSessionState.Active
            });

        _mockStore
            .Setup(s => s.GetTerminalAuditEventsAsync(sessionId, 0L, 2000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TerminalAuditEventDocument>());

        await _service.AttachSessionAsync("conn-2", sessionId, 0);

        _mockStore.Verify(s => s.GetTerminalSessionAsync(sessionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task AttachSession_WhenSessionNotFound_ThrowsInvalidOperation()
    {
        _mockStore
            .Setup(s => s.GetTerminalSessionAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TerminalSessionDocument?)null);

        var act = () => _service.AttachSessionAsync("conn-1", "nonexistent", 0);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
    }

    [Test]
    public async Task AttachSession_WhenSessionClosed_ThrowsInvalidOperation()
    {
        _mockStore
            .Setup(s => s.GetTerminalSessionAsync("closed-session", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TerminalSessionDocument
            {
                Id = "closed-session",
                WorkerId = "worker-1",
                State = TerminalSessionState.Closed
            });

        var act = () => _service.AttachSessionAsync("conn-1", "closed-session", 0);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*closed*");
    }

    [Test]
    public async Task SendInput_PersistsAuditEvent()
    {
        var sessionId = await _service.CreateSessionAsync("conn-1", "worker-1", null, 80, 24);

        await _service.SendInputAsync(sessionId, "aGVsbG8=");

        _mockStore.Verify(s => s.AppendTerminalAuditEventAsync(
            It.Is<TerminalAuditEventDocument>(e =>
                e.SessionId == sessionId &&
                e.Direction == TerminalDataDirection.Input &&
                e.PayloadBase64 == "aGVsbG8="),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task SendInput_ForwardsToWorkerHub()
    {
        var sessionId = await _service.CreateSessionAsync("conn-1", "worker-1", null, 80, 24);

        await _service.SendInputAsync(sessionId, "aGVsbG8=");

        _mockTerminalHub.Verify(h => h.SendInputAsync(
            It.Is<TerminalInputMessage>(m =>
                m.SessionId == sessionId &&
                m.PayloadBase64 == "aGVsbG8=")), Times.Once);
    }

    [Test]
    public async Task SendInput_WithNoActiveBridge_ThrowsInvalidOperation()
    {
        var act = () => _service.SendInputAsync("unknown-session", "data");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No active bridge*");
    }

    [Test]
    public async Task Resize_UpdatesStoreAndForwardsToWorker()
    {
        var sessionId = await _service.CreateSessionAsync("conn-1", "worker-1", null, 80, 24);

        _mockStore
            .Setup(s => s.GetTerminalSessionAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TerminalSessionDocument
            {
                Id = sessionId,
                WorkerId = "worker-1",
                State = TerminalSessionState.Active,
                Cols = 80,
                Rows = 24
            });

        await _service.ResizeAsync(sessionId, 120, 40);

        _mockStore.Verify(s => s.UpdateTerminalSessionAsync(
            It.Is<TerminalSessionDocument>(d => d.Cols == 120 && d.Rows == 40),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockTerminalHub.Verify(h => h.ResizeSessionAsync(
            It.Is<TerminalResizeMessage>(m =>
                m.SessionId == sessionId &&
                m.Cols == 120 &&
                m.Rows == 40)), Times.Once);
    }

    [Test]
    public async Task Close_SendsCloseToWorkerAndUpdatesStore()
    {
        var sessionId = await _service.CreateSessionAsync("conn-1", "worker-1", null, 80, 24);

        await _service.CloseAsync(sessionId);

        _mockTerminalHub.Verify(h => h.CloseSessionAsync(
            It.Is<CloseTerminalSessionRequest>(r => r.SessionId == sessionId)), Times.Once);

        _mockStore.Verify(s => s.CloseTerminalSessionAsync(
            sessionId, "Closed by user", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task OnClientDisconnected_WhenNoSessionMapped_DoesNothing()
    {
        await _service.OnClientDisconnectedAsync("unknown-conn");

        _mockStore.Verify(
            s => s.UpdateTerminalSessionAsync(It.IsAny<TerminalSessionDocument>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task OnClientDisconnected_WhenLastClient_UpdatesSessionToDisconnected()
    {
        var sessionId = await _service.CreateSessionAsync("conn-only", "worker-1", null, 80, 24);

        _mockStore
            .Setup(s => s.GetTerminalSessionAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TerminalSessionDocument
            {
                Id = sessionId,
                WorkerId = "worker-1",
                State = TerminalSessionState.Active
            });

        await _service.OnClientDisconnectedAsync("conn-only");

        _mockStore.Verify(s => s.UpdateTerminalSessionAsync(
            It.Is<TerminalSessionDocument>(d =>
                d.State == TerminalSessionState.Disconnected),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task CreateSession_SecondHubCall_ReusesConnection()
    {
        await _service.CreateSessionAsync("conn-1", "worker-1", null, 80, 24);
        await _service.CreateSessionAsync("conn-2", "worker-2", null, 80, 24);

        // Should only connect once
        _mockClientFactory.Verify(
            f => f.ConnectTerminalHubAsync(It.IsAny<ITerminalReceiver>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task HandleSessionOutput_PersistsAuditAndBroadcasts()
    {
        var sessionId = await _service.CreateSessionAsync("conn-output", "worker-1", null, 80, 24);

        // Trigger the receiver callback
        ((ITerminalReceiver)_service).OnSessionOutput(new TerminalOutputMessage
        {
            SessionId = sessionId,
            Sequence = 1,
            PayloadBase64 = "b3V0cHV0",
            Direction = TerminalDataDirection.Output
        });

        // Give the async handler a moment to run
        await Task.Delay(100);

        _mockStore.Verify(s => s.AppendTerminalAuditEventAsync(
            It.Is<TerminalAuditEventDocument>(e =>
                e.SessionId == sessionId &&
                e.Direction == TerminalDataDirection.Output &&
                e.PayloadBase64 == "b3V0cHV0"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task HandleSessionClosed_UpdatesStoreAndNotifiesClients()
    {
        var sessionId = await _service.CreateSessionAsync("conn-close", "worker-1", null, 80, 24);

        ((ITerminalReceiver)_service).OnSessionClosed(new TerminalSessionClosedMessage
        {
            SessionId = sessionId,
            Reason = "Process exited"
        });

        await Task.Delay(100);

        _mockStore.Verify(s => s.CloseTerminalSessionAsync(
            sessionId, "Process exited", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task HandleSessionError_NotifiesConnectedClients()
    {
        var sessionId = await _service.CreateSessionAsync("conn-error", "worker-1", null, 80, 24);

        ((ITerminalReceiver)_service).OnSessionError(new TerminalSessionErrorMessage
        {
            SessionId = sessionId,
            Error = "Container not found"
        });

        await Task.Delay(100);

        _mockClientProxy.Verify(
            c => c.SendCoreAsync("TerminalSessionError", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task HandleSessionOpened_Success_UpdatesSessionToActive()
    {
        var sessionId = await _service.CreateSessionAsync("conn-open", "worker-1", null, 80, 24);

        _mockStore
            .Setup(s => s.GetTerminalSessionAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TerminalSessionDocument
            {
                Id = sessionId,
                WorkerId = "worker-1",
                State = TerminalSessionState.Pending
            });

        ((ITerminalReceiver)_service).OnSessionOpened(new TerminalSessionOpenedMessage
        {
            SessionId = sessionId,
            Success = true
        });

        await Task.Delay(100);

        _mockStore.Verify(s => s.UpdateTerminalSessionAsync(
            It.Is<TerminalSessionDocument>(d => d.State == TerminalSessionState.Active),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task HandleSessionOpened_Failure_UpdatesSessionToClosed()
    {
        var sessionId = await _service.CreateSessionAsync("conn-fail", "worker-1", null, 80, 24);

        _mockStore
            .Setup(s => s.GetTerminalSessionAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TerminalSessionDocument
            {
                Id = sessionId,
                WorkerId = "worker-1",
                State = TerminalSessionState.Pending
            });

        ((ITerminalReceiver)_service).OnSessionOpened(new TerminalSessionOpenedMessage
        {
            SessionId = sessionId,
            Success = false,
            Error = "Container crashed"
        });

        await Task.Delay(100);

        _mockStore.Verify(s => s.UpdateTerminalSessionAsync(
            It.Is<TerminalSessionDocument>(d =>
                d.State == TerminalSessionState.Closed &&
                d.CloseReason == "Container crashed"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}

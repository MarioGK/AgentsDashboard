using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.WorkerGateway.MagicOnion;
using AgentsDashboard.WorkerGateway.Services;
using MagicOnion.Server.Hubs;
using Microsoft.Extensions.Logging;

namespace AgentsDashboard.UnitTests.WorkerGateway.MagicOnion;

public class TerminalHubTests
{
    // ── Type contract tests ──────────────────────────────────────────────

    [Test]
    public void TerminalHub_ImplementsITerminalHub()
    {
        typeof(TerminalHub).Should().BeAssignableTo<ITerminalHub>();
    }

    [Test]
    public void TerminalHub_ExtendsStreamingHubBase()
    {
        typeof(TerminalHub).Should().BeAssignableTo<StreamingHubBase<ITerminalHub, ITerminalReceiver>>();
    }

    [Test]
    public void TerminalHub_IsSealed()
    {
        typeof(TerminalHub).IsSealed.Should().BeTrue();
    }

    [Test]
    public void TerminalHub_HasCorrectConstructorParameters()
    {
        var ctor = typeof(TerminalHub).GetConstructors();

        ctor.Should().ContainSingle();
        var parameters = ctor[0].GetParameters();
        parameters.Should().HaveCount(2);
        parameters[0].ParameterType.Should().Be(typeof(ITerminalSessionManager));
        parameters[1].ParameterType.Should().Be(typeof(ILogger<TerminalHub>));
    }

    [Test]
    public void TerminalHub_HasOpenSessionAsyncMethod()
    {
        var method = typeof(TerminalHub).GetMethod("OpenSessionAsync");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task));
        method.GetParameters().Should().ContainSingle()
            .Which.ParameterType.Should().Be(typeof(OpenTerminalSessionRequest));
    }

    [Test]
    public void TerminalHub_HasSendInputAsyncMethod()
    {
        var method = typeof(TerminalHub).GetMethod("SendInputAsync");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task));
        method.GetParameters().Should().ContainSingle()
            .Which.ParameterType.Should().Be(typeof(TerminalInputMessage));
    }

    [Test]
    public void TerminalHub_HasResizeSessionAsyncMethod()
    {
        var method = typeof(TerminalHub).GetMethod("ResizeSessionAsync");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task));
        method.GetParameters().Should().ContainSingle()
            .Which.ParameterType.Should().Be(typeof(TerminalResizeMessage));
    }

    [Test]
    public void TerminalHub_HasCloseSessionAsyncMethod()
    {
        var method = typeof(TerminalHub).GetMethod("CloseSessionAsync");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task));
        method.GetParameters().Should().ContainSingle()
            .Which.ParameterType.Should().Be(typeof(CloseTerminalSessionRequest));
    }

    [Test]
    public void TerminalHub_HasReattachSessionAsyncMethod()
    {
        var method = typeof(TerminalHub).GetMethod("ReattachSessionAsync");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task));
        method.GetParameters().Should().ContainSingle()
            .Which.ParameterType.Should().Be(typeof(ReattachTerminalSessionRequest));
    }

    [Test]
    public void TerminalHub_HasOnDisconnectedOverride()
    {
        var method = typeof(TerminalHub).GetMethod(
            "OnDisconnected",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        method.Should().NotBeNull();
        method!.IsVirtual.Should().BeTrue();
    }

    [Test]
    public void TerminalHub_HasOnConnectingOverride()
    {
        var method = typeof(TerminalHub).GetMethod(
            "OnConnecting",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        method.Should().NotBeNull();
        method!.IsVirtual.Should().BeTrue();
    }

    // ── ITerminalHub interface contract tests ────────────────────────────

    [Test]
    public void ITerminalHub_HasFiveMethods()
    {
        var methods = typeof(ITerminalHub).GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.DeclaredOnly);

        methods.Should().HaveCount(5);
    }

    [Test]
    public void ITerminalHub_AllMethodsReturnTask()
    {
        var methods = typeof(ITerminalHub).GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.DeclaredOnly);

        foreach (var method in methods)
        {
            method.ReturnType.Should().Be(typeof(Task),
                $"method {method.Name} should return Task");
        }
    }

    // ── ITerminalReceiver contract tests ─────────────────────────────────

    [Test]
    public void ITerminalReceiver_HasOnSessionOpenedMethod()
    {
        var method = typeof(ITerminalReceiver).GetMethod("OnSessionOpened");
        method.Should().NotBeNull();
        method!.GetParameters().Should().ContainSingle()
            .Which.ParameterType.Should().Be(typeof(TerminalSessionOpenedMessage));
    }

    [Test]
    public void ITerminalReceiver_HasOnSessionOutputMethod()
    {
        var method = typeof(ITerminalReceiver).GetMethod("OnSessionOutput");
        method.Should().NotBeNull();
        method!.GetParameters().Should().ContainSingle()
            .Which.ParameterType.Should().Be(typeof(TerminalOutputMessage));
    }

    [Test]
    public void ITerminalReceiver_HasOnSessionClosedMethod()
    {
        var method = typeof(ITerminalReceiver).GetMethod("OnSessionClosed");
        method.Should().NotBeNull();
        method!.GetParameters().Should().ContainSingle()
            .Which.ParameterType.Should().Be(typeof(TerminalSessionClosedMessage));
    }

    [Test]
    public void ITerminalReceiver_HasOnSessionErrorMethod()
    {
        var method = typeof(ITerminalReceiver).GetMethod("OnSessionError");
        method.Should().NotBeNull();
        method!.GetParameters().Should().ContainSingle()
            .Which.ParameterType.Should().Be(typeof(TerminalSessionErrorMessage));
    }

    [Test]
    public void ITerminalReceiver_HasFourMethods()
    {
        var methods = typeof(ITerminalReceiver).GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.DeclaredOnly);

        methods.Should().HaveCount(4);
    }

    // ── Message model tests ──────────────────────────────────────────────

    [Test]
    public void OpenTerminalSessionRequest_CanBeCreated()
    {
        var request = new OpenTerminalSessionRequest
        {
            SessionId = "session-1",
            WorkerId = "worker-1",
            RunId = "run-1",
            Cols = 120,
            Rows = 40,
        };

        request.SessionId.Should().Be("session-1");
        request.WorkerId.Should().Be("worker-1");
        request.RunId.Should().Be("run-1");
        request.Cols.Should().Be(120);
        request.Rows.Should().Be(40);
    }

    [Test]
    public void OpenTerminalSessionRequest_DefaultDimensions()
    {
        var request = new OpenTerminalSessionRequest
        {
            SessionId = "s1",
            WorkerId = "w1",
        };

        request.Cols.Should().Be(80);
        request.Rows.Should().Be(24);
    }

    [Test]
    public void OpenTerminalSessionRequest_RunIdIsOptional()
    {
        var request = new OpenTerminalSessionRequest
        {
            SessionId = "s1",
            WorkerId = "w1",
        };

        request.RunId.Should().BeNull();
    }

    [Test]
    public void TerminalInputMessage_CanBeCreated()
    {
        var payload = Convert.ToBase64String("hello"u8.ToArray());
        var message = new TerminalInputMessage
        {
            SessionId = "session-1",
            PayloadBase64 = payload,
        };

        message.SessionId.Should().Be("session-1");
        message.PayloadBase64.Should().Be(payload);
    }

    [Test]
    public void TerminalInputMessage_PayloadCanBeDecoded()
    {
        var original = "echo test\n";
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(original));
        var message = new TerminalInputMessage
        {
            SessionId = "s1",
            PayloadBase64 = encoded,
        };

        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(message.PayloadBase64));
        decoded.Should().Be(original);
    }

    [Test]
    public void TerminalResizeMessage_CanBeCreated()
    {
        var message = new TerminalResizeMessage
        {
            SessionId = "session-1",
            Cols = 200,
            Rows = 50,
        };

        message.SessionId.Should().Be("session-1");
        message.Cols.Should().Be(200);
        message.Rows.Should().Be(50);
    }

    [Test]
    public void CloseTerminalSessionRequest_CanBeCreated()
    {
        var request = new CloseTerminalSessionRequest
        {
            SessionId = "session-1",
        };

        request.SessionId.Should().Be("session-1");
    }

    [Test]
    public void ReattachTerminalSessionRequest_CanBeCreated()
    {
        var request = new ReattachTerminalSessionRequest
        {
            SessionId = "session-1",
            LastSequence = 42,
        };

        request.SessionId.Should().Be("session-1");
        request.LastSequence.Should().Be(42);
    }

    [Test]
    public void ReattachTerminalSessionRequest_DefaultLastSequence()
    {
        var request = new ReattachTerminalSessionRequest
        {
            SessionId = "s1",
        };

        request.LastSequence.Should().Be(0);
    }

    [Test]
    public void TerminalSessionOpenedMessage_CanBeCreated()
    {
        var message = new TerminalSessionOpenedMessage
        {
            SessionId = "session-1",
            Success = true,
        };

        message.SessionId.Should().Be("session-1");
        message.Success.Should().BeTrue();
        message.Error.Should().BeNull();
    }

    [Test]
    public void TerminalSessionOpenedMessage_WithError()
    {
        var message = new TerminalSessionOpenedMessage
        {
            SessionId = "session-1",
            Success = false,
            Error = "Container not found",
        };

        message.Success.Should().BeFalse();
        message.Error.Should().Be("Container not found");
    }

    [Test]
    public void TerminalOutputMessage_CanBeCreated()
    {
        var payload = Convert.ToBase64String("output data"u8.ToArray());
        var message = new TerminalOutputMessage
        {
            SessionId = "session-1",
            Sequence = 5,
            PayloadBase64 = payload,
            Direction = TerminalDataDirection.Output,
        };

        message.SessionId.Should().Be("session-1");
        message.Sequence.Should().Be(5);
        message.PayloadBase64.Should().Be(payload);
        message.Direction.Should().Be(TerminalDataDirection.Output);
    }

    [Test]
    public void TerminalSessionClosedMessage_CanBeCreated()
    {
        var message = new TerminalSessionClosedMessage
        {
            SessionId = "session-1",
            Reason = "User closed session",
        };

        message.SessionId.Should().Be("session-1");
        message.Reason.Should().Be("User closed session");
    }

    [Test]
    public void TerminalSessionClosedMessage_ReasonIsOptional()
    {
        var message = new TerminalSessionClosedMessage
        {
            SessionId = "s1",
        };

        message.Reason.Should().BeNull();
    }

    [Test]
    public void TerminalSessionErrorMessage_CanBeCreated()
    {
        var message = new TerminalSessionErrorMessage
        {
            SessionId = "session-1",
            Error = "Docker daemon unreachable",
        };

        message.SessionId.Should().Be("session-1");
        message.Error.Should().Be("Docker daemon unreachable");
    }

    // ── ITerminalSessionManager mock interaction tests ───────────────────

    [Test]
    public void ITerminalSessionManager_CanBeMocked()
    {
        var mock = new Mock<ITerminalSessionManager>();

        mock.Should().NotBeNull();
        mock.Object.Should().BeAssignableTo<ITerminalSessionManager>();
    }

    [Test]
    public async Task ITerminalSessionManager_MockOpenSession_ReturnsSessionInfo()
    {
        var mock = new Mock<ITerminalSessionManager>();
        var expectedSession = new TerminalSessionInfo
        {
            SessionId = "session-1",
            ContainerId = "container-abc",
            ExecId = "exec-xyz",
            RunId = "run-1",
            Cols = 80,
            Rows = 24,
        };

        mock.Setup(m => m.OpenSessionAsync(
                "session-1", "run-1", 80, 24, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedSession);

        var result = await mock.Object.OpenSessionAsync(
            "session-1", "run-1", 80, 24, CancellationToken.None);

        result.SessionId.Should().Be("session-1");
        result.ContainerId.Should().Be("container-abc");
        result.RunId.Should().Be("run-1");

        mock.Verify(m => m.OpenSessionAsync(
            "session-1", "run-1", 80, 24, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ITerminalSessionManager_MockSendInput_Verifiable()
    {
        var mock = new Mock<ITerminalSessionManager>();
        var data = "test input"u8.ToArray();

        mock.Setup(m => m.SendInputAsync("session-1", It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await mock.Object.SendInputAsync("session-1", data, CancellationToken.None);

        mock.Verify(m => m.SendInputAsync("session-1", It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ITerminalSessionManager_MockResize_Verifiable()
    {
        var mock = new Mock<ITerminalSessionManager>();

        mock.Setup(m => m.ResizeAsync("session-1", 120, 40, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await mock.Object.ResizeAsync("session-1", 120, 40, CancellationToken.None);

        mock.Verify(m => m.ResizeAsync("session-1", 120, 40, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ITerminalSessionManager_MockClose_Verifiable()
    {
        var mock = new Mock<ITerminalSessionManager>();

        mock.Setup(m => m.CloseSessionAsync("session-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await mock.Object.CloseSessionAsync("session-1", CancellationToken.None);

        mock.Verify(m => m.CloseSessionAsync("session-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void ITerminalSessionManager_MockTryGetSession_WhenFound_ReturnsTrue()
    {
        var mock = new Mock<ITerminalSessionManager>();
        var session = new TerminalSessionInfo
        {
            SessionId = "session-1",
            ContainerId = "c1",
            ExecId = "e1",
        };

        mock.Setup(m => m.TryGetSession("session-1", out session))
            .Returns(true);

        var found = mock.Object.TryGetSession("session-1", out var result);

        found.Should().BeTrue();
        result.Should().NotBeNull();
        result!.SessionId.Should().Be("session-1");
    }

    [Test]
    public void ITerminalSessionManager_MockTryGetSession_WhenNotFound_ReturnsFalse()
    {
        var mock = new Mock<ITerminalSessionManager>();
        TerminalSessionInfo? nullSession = null;

        mock.Setup(m => m.TryGetSession("nonexistent", out nullSession))
            .Returns(false);

        var found = mock.Object.TryGetSession("nonexistent", out var result);

        found.Should().BeFalse();
        result.Should().BeNull();
    }

    [Test]
    public void ITerminalSessionManager_MockRegisterCallback_Verifiable()
    {
        var mock = new Mock<ITerminalSessionManager>();

        mock.Object.RegisterOutputCallback("session-1",
            (_, _, _) => Task.CompletedTask);

        mock.Verify(m => m.RegisterOutputCallback("session-1",
            It.IsAny<Func<byte[], TerminalDataDirection, CancellationToken, Task>>()), Times.Once);
    }

    [Test]
    public void ITerminalSessionManager_MockUnregisterCallback_Verifiable()
    {
        var mock = new Mock<ITerminalSessionManager>();

        mock.Object.UnregisterOutputCallback("session-1");

        mock.Verify(m => m.UnregisterOutputCallback("session-1"), Times.Once);
    }

    [Test]
    public async Task ITerminalSessionManager_MockOpenSession_WhenThrows_PropagatesException()
    {
        var mock = new Mock<ITerminalSessionManager>();
        mock.Setup(m => m.OpenSessionAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("No running container found"));

        var action = () => mock.Object.OpenSessionAsync(
            "session-1", "run-missing", 80, 24, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No running container found*");
    }

    [Test]
    public async Task ITerminalSessionManager_MockOpenSession_WhenMaxReached_ThrowsInvalidOperation()
    {
        var mock = new Mock<ITerminalSessionManager>();
        mock.Setup(m => m.OpenSessionAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Maximum concurrent sessions (20) reached"));

        var action = () => mock.Object.OpenSessionAsync(
            "session-overflow", null, 80, 24, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Maximum concurrent sessions*");
    }
}

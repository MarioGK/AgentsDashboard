using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Hubs;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.AspNetCore.SignalR;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class SignalRRunEventPublisherTests
{
    private readonly Mock<IHubContext<RunEventsHub>> _mockHubContext;
    private readonly Mock<IHubClients> _mockClients;
    private readonly Mock<IClientProxy> _mockClientProxy;
    private readonly SignalRRunEventPublisher _publisher;

    public SignalRRunEventPublisherTests()
    {
        _mockHubContext = new Mock<IHubContext<RunEventsHub>>();
        _mockClients = new Mock<IHubClients>();
        _mockClientProxy = new Mock<IClientProxy>();

        _mockHubContext.Setup(x => x.Clients).Returns(_mockClients.Object);
        _mockClients.Setup(x => x.All).Returns(_mockClientProxy.Object);

        _publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
    }

    private static RunDocument CreateRun(
        string id = "",
        RunState state = RunState.Queued,
        string summary = "",
        DateTime? startedAt = null,
        DateTime? endedAt = null)
    {
        return new RunDocument
        {
            Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("N") : id,
            State = state,
            Summary = summary,
            StartedAtUtc = startedAt,
            EndedAtUtc = endedAt
        };
    }

    private static RunLogEvent CreateLogEvent(
        string runId = "",
        string level = "info",
        string message = "",
        DateTime? timestamp = null)
    {
        return new RunLogEvent
        {
            RunId = string.IsNullOrEmpty(runId) ? Guid.NewGuid().ToString("N") : runId,
            Level = level,
            Message = message,
            TimestampUtc = timestamp ?? DateTime.UtcNow
        };
    }

    [Test]
    public void Constructor_WithValidHubContext_CreatesInstance()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);

        publisher.Should().NotBeNull();
        publisher.Should().BeAssignableTo<IRunEventPublisher>();
    }

    [Test]
    public async Task PublishStatusAsync_SendsCorrectMethodName()
    {
        var run = CreateRun();

        await _publisher.PublishStatusAsync(run, CancellationToken.None);

        _mockClientProxy.Verify(
            x => x.SendCoreAsync("RunStatusChanged", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task PublishStatusAsync_SendsCorrectParameters()
    {
        var startedAt = DateTime.UtcNow.AddMinutes(-5);
        var endedAt = DateTime.UtcNow;
        var run = CreateRun(
            id: "run-123",
            state: RunState.Running,
            summary: "Test run",
            startedAt: startedAt,
            endedAt: endedAt);

        object[]? capturedArgs = null;
        _mockClientProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args);

        await _publisher.PublishStatusAsync(run, CancellationToken.None);

        capturedArgs.Should().NotBeNull();
        capturedArgs!.Length.Should().Be(5);
        ((string)capturedArgs[0]).Should().Be("run-123");
        ((string)capturedArgs[1]).Should().Be("Running");
        ((string)capturedArgs[2]).Should().Be("Test run");
        capturedArgs[3].Should().Be(startedAt);
        capturedArgs[4].Should().Be(endedAt);
    }

    [Test]
    public async Task PublishStatusAsync_WithNullDates_SendsNullValues()
    {
        var run = CreateRun(
            id: "run-456",
            state: RunState.Queued,
            startedAt: null,
            endedAt: null);

        object[]? capturedArgs = null;
        _mockClientProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args);

        await _publisher.PublishStatusAsync(run, CancellationToken.None);

        capturedArgs.Should().NotBeNull();
        capturedArgs![3].Should().BeNull();
        capturedArgs[4].Should().BeNull();
    }

    [Test]
    [Arguments(RunState.Queued, "Queued")]
    [Arguments(RunState.Running, "Running")]
    [Arguments(RunState.Succeeded, "Succeeded")]
    [Arguments(RunState.Failed, "Failed")]
    [Arguments(RunState.Cancelled, "Cancelled")]
    [Arguments(RunState.PendingApproval, "PendingApproval")]
    public async Task PublishStatusAsync_WithDifferentStates_SendsCorrectStateString(RunState state, string expected)
    {
        var run = CreateRun(state: state);

        object[]? capturedArgs = null;
        _mockClientProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args);

        await _publisher.PublishStatusAsync(run, CancellationToken.None);

        capturedArgs.Should().NotBeNull();
        ((string)capturedArgs![1]).Should().Be(expected);
    }

    [Test]
    public async Task PublishStatusAsync_WithCancellationToken_PassesToken()
    {
        var run = CreateRun();
        using var cts = new CancellationTokenSource();

        await _publisher.PublishStatusAsync(run, cts.Token);

        _mockClientProxy.Verify(
            x => x.SendCoreAsync("RunStatusChanged", It.IsAny<object[]>(), cts.Token),
            Times.Once);
    }

    [Test]
    public async Task PublishStatusAsync_CallsClientsAll()
    {
        var run = CreateRun();

        await _publisher.PublishStatusAsync(run, CancellationToken.None);

        _mockClients.Verify(x => x.All, Times.Once);
    }

    [Test]
    public async Task PublishStatusAsync_WithEmptySummary_SendsEmptyString()
    {
        var run = CreateRun(summary: "");

        object[]? capturedArgs = null;
        _mockClientProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args);

        await _publisher.PublishStatusAsync(run, CancellationToken.None);

        capturedArgs.Should().NotBeNull();
        ((string)capturedArgs![2]).Should().Be("");
    }

    [Test]
    public async Task PublishLogAsync_SendsCorrectMethodName()
    {
        var logEvent = CreateLogEvent();

        await _publisher.PublishLogAsync(logEvent, CancellationToken.None);

        _mockClientProxy.Verify(
            x => x.SendCoreAsync("RunLogChunk", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task PublishLogAsync_SendsCorrectParameters()
    {
        var timestamp = DateTime.UtcNow;
        var logEvent = CreateLogEvent(
            runId: "run-789",
            level: "error",
            message: "An error occurred",
            timestamp: timestamp);

        object[]? capturedArgs = null;
        _mockClientProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args);

        await _publisher.PublishLogAsync(logEvent, CancellationToken.None);

        capturedArgs.Should().NotBeNull();
        capturedArgs!.Length.Should().Be(4);
        ((string)capturedArgs[0]).Should().Be("run-789");
        ((string)capturedArgs[1]).Should().Be("error");
        ((string)capturedArgs[2]).Should().Be("An error occurred");
        capturedArgs[3].Should().Be(timestamp);
    }

    [Test]
    [Arguments("debug")]
    [Arguments("info")]
    [Arguments("warn")]
    [Arguments("error")]
    [Arguments("chunk")]
    public async Task PublishLogAsync_WithDifferentLevels_SendsCorrectLevel(string level)
    {
        var logEvent = CreateLogEvent(level: level);

        object[]? capturedArgs = null;
        _mockClientProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args);

        await _publisher.PublishLogAsync(logEvent, CancellationToken.None);

        capturedArgs.Should().NotBeNull();
        ((string)capturedArgs![1]).Should().Be(level);
    }

    [Test]
    public async Task PublishLogAsync_WithCancellationToken_PassesToken()
    {
        var logEvent = CreateLogEvent();
        using var cts = new CancellationTokenSource();

        await _publisher.PublishLogAsync(logEvent, cts.Token);

        _mockClientProxy.Verify(
            x => x.SendCoreAsync("RunLogChunk", It.IsAny<object[]>(), cts.Token),
            Times.Once);
    }

    [Test]
    public async Task PublishLogAsync_CallsClientsAll()
    {
        var logEvent = CreateLogEvent();

        await _publisher.PublishLogAsync(logEvent, CancellationToken.None);

        _mockClients.Verify(x => x.All, Times.Once);
    }

    [Test]
    public async Task PublishLogAsync_WithEmptyMessage_SendsEmptyString()
    {
        var logEvent = CreateLogEvent(message: "");

        object[]? capturedArgs = null;
        _mockClientProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args);

        await _publisher.PublishLogAsync(logEvent, CancellationToken.None);

        capturedArgs.Should().NotBeNull();
        ((string)capturedArgs![2]).Should().Be("");
    }

    [Test]
    public async Task PublishLogAsync_WithMultilineMessage_PreservesNewlines()
    {
        var multilineMessage = "Line 1\nLine 2\nLine 3";
        var logEvent = CreateLogEvent(message: multilineMessage);

        object[]? capturedArgs = null;
        _mockClientProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args);

        await _publisher.PublishLogAsync(logEvent, CancellationToken.None);

        capturedArgs.Should().NotBeNull();
        ((string)capturedArgs![2]).Should().Be(multilineMessage);
    }

    [Test]
    public async Task PublishStatusAsync_MultipleSequentialCalls_SendsAllEvents()
    {
        for (int i = 0; i < 5; i++)
        {
            var run = CreateRun(id: $"run-{i}");
            await _publisher.PublishStatusAsync(run, CancellationToken.None);
        }

        _mockClientProxy.Verify(
            x => x.SendCoreAsync("RunStatusChanged", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Exactly(5));
    }

    [Test]
    public async Task PublishLogAsync_MultipleSequentialCalls_SendsAllEvents()
    {
        for (int i = 0; i < 5; i++)
        {
            var logEvent = CreateLogEvent(runId: $"run-{i}");
            await _publisher.PublishLogAsync(logEvent, CancellationToken.None);
        }

        _mockClientProxy.Verify(
            x => x.SendCoreAsync("RunLogChunk", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Exactly(5));
    }

    [Test]
    public async Task PublishStatusAsync_ConcurrentCalls_AllSucceed()
    {
        var runs = Enumerable.Range(0, 10)
            .Select(i => CreateRun(id: $"run-{i}"))
            .ToList();

        var tasks = runs.Select(run => _publisher.PublishStatusAsync(run, CancellationToken.None));
        await Task.WhenAll(tasks);

        _mockClientProxy.Verify(
            x => x.SendCoreAsync("RunStatusChanged", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Exactly(10));
    }

    [Test]
    public async Task PublishLogAsync_ConcurrentCalls_AllSucceed()
    {
        var logEvents = Enumerable.Range(0, 10)
            .Select(i => CreateLogEvent(runId: $"run-{i}"))
            .ToList();

        var tasks = logEvents.Select(log => _publisher.PublishLogAsync(log, CancellationToken.None));
        await Task.WhenAll(tasks);

        _mockClientProxy.Verify(
            x => x.SendCoreAsync("RunLogChunk", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Exactly(10));
    }

    [Test]
    public async Task PublishStatusAsync_WithCancelledToken_StillAttemptsSend()
    {
        var run = CreateRun();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await _publisher.PublishStatusAsync(run, cts.Token);

        _mockClientProxy.Verify(
            x => x.SendCoreAsync("RunStatusChanged", It.IsAny<object[]>(), cts.Token),
            Times.Once);
    }

    [Test]
    public async Task PublishLogAsync_WithCancelledToken_StillAttemptsSend()
    {
        var logEvent = CreateLogEvent();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await _publisher.PublishLogAsync(logEvent, cts.Token);

        _mockClientProxy.Verify(
            x => x.SendCoreAsync("RunLogChunk", It.IsAny<object[]>(), cts.Token),
            Times.Once);
    }

    [Test]
    public async Task PublishStatusAsync_MixedWithPublishLogAsync_BothSucceed()
    {
        var run = CreateRun(id: "run-mixed", state: RunState.Running);
        var logEvent = CreateLogEvent(runId: "run-mixed", level: "info");

        await _publisher.PublishStatusAsync(run, CancellationToken.None);
        await _publisher.PublishLogAsync(logEvent, CancellationToken.None);
        await _publisher.PublishStatusAsync(run, CancellationToken.None);

        _mockClientProxy.Verify(
            x => x.SendCoreAsync("RunStatusChanged", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        _mockClientProxy.Verify(
            x => x.SendCoreAsync("RunLogChunk", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task PublishStatusAsync_WithLongSummary_SendsFullSummary()
    {
        var longSummary = new string('A', 1000);
        var run = CreateRun(summary: longSummary);

        object[]? capturedArgs = null;
        _mockClientProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args);

        await _publisher.PublishStatusAsync(run, CancellationToken.None);

        capturedArgs.Should().NotBeNull();
        ((string)capturedArgs![2]).Should().Be(longSummary);
    }

    [Test]
    public async Task PublishLogAsync_WithLongMessage_SendsFullMessage()
    {
        var longMessage = new string('B', 5000);
        var logEvent = CreateLogEvent(message: longMessage);

        object[]? capturedArgs = null;
        _mockClientProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args);

        await _publisher.PublishLogAsync(logEvent, CancellationToken.None);

        capturedArgs.Should().NotBeNull();
        ((string)capturedArgs![2]).Should().Be(longMessage);
    }
}

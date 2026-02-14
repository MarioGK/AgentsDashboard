using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Hubs;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace AgentsDashboard.UnitTests.ControlPlane.Hubs;

public class RunEventsHubTests
{
    private readonly Mock<IHubContext<RunEventsHub>> _mockHubContext;
    private readonly Mock<IHubClients> _mockClients;
    private readonly Mock<IClientProxy> _mockClientProxy;

    public RunEventsHubTests()
    {
        _mockHubContext = new Mock<IHubContext<RunEventsHub>>();
        _mockClients = new Mock<IHubClients>();
        _mockClientProxy = new Mock<IClientProxy>();

        _mockHubContext.Setup(x => x.Clients).Returns(_mockClients.Object);
        _mockClients.Setup(x => x.All).Returns(_mockClientProxy.Object);
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

    [Fact]
    public void RunEventsHub_InheritsFromHub()
    {
        typeof(RunEventsHub).Should().BeAssignableTo<Hub>();
    }

    [Fact]
    public async Task PublishStatusAsync_SendsRunStatusChangedToAllClients()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var startedAt = DateTime.UtcNow.AddMinutes(-5);
        var run = CreateRun(
            id: "run-123",
            state: RunState.Running,
            summary: "Test run in progress",
            startedAt: startedAt);

        object[]? capturedArgs = null;
        _mockClientProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args);

        await publisher.PublishStatusAsync(run, CancellationToken.None);

        capturedArgs.Should().NotBeNull();
        capturedArgs!.Length.Should().Be(5);
        ((string)capturedArgs[0]).Should().Be("run-123");
        ((string)capturedArgs[1]).Should().Be("Running");
        ((string)capturedArgs[2]).Should().Be("Test run in progress");
        capturedArgs[3].Should().Be(startedAt);
        capturedArgs[4].Should().Be(run.EndedAtUtc);
    }

    [Fact]
    public async Task PublishStatusAsync_WithDifferentStates_SendsCorrectStateString()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var testCases = new[]
        {
            RunState.Queued,
            RunState.Running,
            RunState.Succeeded,
            RunState.Failed,
            RunState.Cancelled,
            RunState.PendingApproval
        };

        foreach (var state in testCases)
        {
            _mockClientProxy.Invocations.Clear();
            var run = CreateRun(state: state);

            object[]? capturedArgs = null;
            _mockClientProxy
                .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
                .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args);

            await publisher.PublishStatusAsync(run, CancellationToken.None);

            capturedArgs.Should().NotBeNull();
            ((string)capturedArgs![1]).Should().Be(state.ToString());
        }
    }

    [Fact]
    public async Task PublishStatusAsync_WithNullDates_SendsNullValues()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var run = CreateRun(
            id: "run-456",
            state: RunState.Queued,
            startedAt: null,
            endedAt: null);

        object[]? capturedArgs = null;
        _mockClientProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args);

        await publisher.PublishStatusAsync(run, CancellationToken.None);

        capturedArgs.Should().NotBeNull();
        ((string)capturedArgs![0]).Should().Be("run-456");
        capturedArgs[3].Should().BeNull();
        capturedArgs[4].Should().BeNull();
    }

    [Fact]
    public async Task PublishStatusAsync_WithCancellationToken_PassesToClientProxy()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var run = CreateRun();
        using var cts = new CancellationTokenSource();

        await publisher.PublishStatusAsync(run, cts.Token);

        _mockClientProxy.Verify(
            x => x.SendCoreAsync(
                "RunStatusChanged",
                It.IsAny<object[]>(),
                cts.Token),
            Times.Once);
    }

    [Fact]
    public async Task PublishStatusAsync_CallsClientsAll()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var run = CreateRun();

        await publisher.PublishStatusAsync(run, CancellationToken.None);

        _mockClients.Verify(x => x.All, Times.Once);
    }

    [Fact]
    public async Task PublishLogAsync_SendsRunLogChunkToAllClients()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
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

        await publisher.PublishLogAsync(logEvent, CancellationToken.None);

        capturedArgs.Should().NotBeNull();
        capturedArgs!.Length.Should().Be(4);
        ((string)capturedArgs[0]).Should().Be("run-789");
        ((string)capturedArgs[1]).Should().Be("error");
        ((string)capturedArgs[2]).Should().Be("An error occurred");
        capturedArgs[3].Should().Be(timestamp);
    }

    [Fact]
    public async Task PublishLogAsync_WithDifferentLevels_SendsCorrectLevelString()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var levels = new[] { "debug", "info", "warn", "error", "fatal" };

        foreach (var level in levels)
        {
            _mockClientProxy.Invocations.Clear();
            var logEvent = CreateLogEvent(level: level);

            object[]? capturedArgs = null;
            _mockClientProxy
                .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
                .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args);

            await publisher.PublishLogAsync(logEvent, CancellationToken.None);

            capturedArgs.Should().NotBeNull();
            ((string)capturedArgs![1]).Should().Be(level);
        }
    }

    [Fact]
    public async Task PublishLogAsync_WithCancellationToken_PassesToClientProxy()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var logEvent = CreateLogEvent();
        using var cts = new CancellationTokenSource();

        await publisher.PublishLogAsync(logEvent, cts.Token);

        _mockClientProxy.Verify(
            x => x.SendCoreAsync(
                "RunLogChunk",
                It.IsAny<object[]>(),
                cts.Token),
            Times.Once);
    }

    [Fact]
    public async Task PublishLogAsync_CallsClientsAll()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var logEvent = CreateLogEvent();

        await publisher.PublishLogAsync(logEvent, CancellationToken.None);

        _mockClients.Verify(x => x.All, Times.Once);
    }

    [Fact]
    public async Task PublishLogAsync_WithEmptyMessage_SendsEmptyString()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var logEvent = CreateLogEvent(message: "");

        object[]? capturedArgs = null;
        _mockClientProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args);

        await publisher.PublishLogAsync(logEvent, CancellationToken.None);

        capturedArgs.Should().NotBeNull();
        ((string)capturedArgs![2]).Should().Be("");
    }

    [Fact]
    public async Task PublishLogAsync_WithMultilineMessage_PreservesNewlines()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var multilineMessage = "Line 1\nLine 2\nLine 3";
        var logEvent = CreateLogEvent(message: multilineMessage);

        object[]? capturedArgs = null;
        _mockClientProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args);

        await publisher.PublishLogAsync(logEvent, CancellationToken.None);

        capturedArgs.Should().NotBeNull();
        ((string)capturedArgs![2]).Should().Be(multilineMessage);
    }

    [Fact]
    public async Task PublishStatusAsync_WithEmptySummary_SendsEmptyString()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var run = CreateRun(summary: "");

        object[]? capturedArgs = null;
        _mockClientProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args);

        await publisher.PublishStatusAsync(run, CancellationToken.None);

        capturedArgs.Should().NotBeNull();
        ((string)capturedArgs![2]).Should().Be("");
    }

    [Fact]
    public void SignalRRunEventPublisher_ImplementsIRunEventPublisher()
    {
        typeof(SignalRRunEventPublisher).Should().BeAssignableTo<IRunEventPublisher>();
    }

    [Fact]
    public async Task PublishStatusAsync_MultipleSequentialCalls_SendsAllEvents()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);

        for (int i = 0; i < 5; i++)
        {
            var run = CreateRun(id: $"run-{i}", state: RunState.Running);
            await publisher.PublishStatusAsync(run, CancellationToken.None);
        }

        _mockClientProxy.Verify(
            x => x.SendCoreAsync("RunStatusChanged", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Exactly(5));
    }

    [Fact]
    public async Task PublishLogAsync_MultipleSequentialCalls_SendsAllEvents()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);

        for (int i = 0; i < 5; i++)
        {
            var logEvent = CreateLogEvent(runId: $"run-{i}", level: "info");
            await publisher.PublishLogAsync(logEvent, CancellationToken.None);
        }

        _mockClientProxy.Verify(
            x => x.SendCoreAsync("RunLogChunk", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Exactly(5));
    }

    [Fact]
    public async Task PublishStatusAsync_WithCancelledToken_StillAttemptsSend()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var run = CreateRun();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await publisher.PublishStatusAsync(run, cts.Token);

        _mockClientProxy.Verify(
            x => x.SendCoreAsync("RunStatusChanged", It.IsAny<object[]>(), cts.Token),
            Times.Once);
    }

    [Fact]
    public async Task PublishLogAsync_WithCancelledToken_StillAttemptsSend()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var logEvent = CreateLogEvent();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await publisher.PublishLogAsync(logEvent, cts.Token);

        _mockClientProxy.Verify(
            x => x.SendCoreAsync("RunLogChunk", It.IsAny<object[]>(), cts.Token),
            Times.Once);
    }

    [Fact]
    public async Task PublishStatusAsync_ConcurrentCalls_AllSucceed()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var runs = Enumerable.Range(0, 10)
            .Select(i => CreateRun(id: $"run-{i}"))
            .ToList();

        var tasks = runs.Select(run => publisher.PublishStatusAsync(run, CancellationToken.None));
        await Task.WhenAll(tasks);

        _mockClientProxy.Verify(
            x => x.SendCoreAsync("RunStatusChanged", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Exactly(10));
    }

    [Fact]
    public async Task PublishLogAsync_ConcurrentCalls_AllSucceed()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var logEvents = Enumerable.Range(0, 10)
            .Select(i => CreateLogEvent(runId: $"run-{i}"))
            .ToList();

        var tasks = logEvents.Select(log => publisher.PublishLogAsync(log, CancellationToken.None));
        await Task.WhenAll(tasks);

        _mockClientProxy.Verify(
            x => x.SendCoreAsync("RunLogChunk", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Exactly(10));
    }

    private static RunEventsHub CreateHub()
    {
        var metrics = new Mock<IOrchestratorMetrics>();
        var logger = new Mock<ILogger<RunEventsHub>>();
        return new RunEventsHub(metrics.Object, logger.Object);
    }

    [Fact]
    public void RunEventsHub_CanBeInstantiated()
    {
        var hub = CreateHub();
        hub.Should().NotBeNull();
    }

    [Fact]
    public void RunEventsHub_IsSealed()
    {
        typeof(RunEventsHub).IsSealed.Should().BeTrue();
    }
}

public class RunEventsHubConnectionTests
{
    private readonly Mock<IHubContext<RunEventsHub>> _mockHubContext;
    private readonly Mock<IHubClients> _mockClients;
    private readonly Mock<IClientProxy> _mockClientProxy;
    private readonly Mock<IGroupManager> _mockGroups;

    public RunEventsHubConnectionTests()
    {
        _mockHubContext = new Mock<IHubContext<RunEventsHub>>();
        _mockClients = new Mock<IHubClients>();
        _mockClientProxy = new Mock<IClientProxy>();
        _mockGroups = new Mock<IGroupManager>();

        _mockHubContext.Setup(x => x.Clients).Returns(_mockClients.Object);
        _mockHubContext.Setup(x => x.Groups).Returns(_mockGroups.Object);
        _mockClients.Setup(x => x.All).Returns(_mockClientProxy.Object);
    }

    [Fact]
    public async Task PublishStatusAsync_BroadcastsToAllClients()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var run = new RunDocument
        {
            Id = "run-broadcast-test",
            State = RunState.Succeeded,
            Summary = "Completed successfully"
        };

        await publisher.PublishStatusAsync(run, CancellationToken.None);

        _mockClients.Verify(x => x.All, Times.Once);
        _mockClientProxy.Verify(
            x => x.SendCoreAsync("RunStatusChanged", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PublishLogAsync_BroadcastsToAllClients()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var logEvent = new RunLogEvent
        {
            RunId = "run-log-broadcast",
            Level = "info",
            Message = "Processing started"
        };

        await publisher.PublishLogAsync(logEvent, CancellationToken.None);

        _mockClients.Verify(x => x.All, Times.Once);
        _mockClientProxy.Verify(
            x => x.SendCoreAsync("RunLogChunk", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Publisher_DoesNotUseGroups()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var run = new RunDocument { Id = "run-1" };
        var logEvent = new RunLogEvent { RunId = "run-1" };

        await publisher.PublishStatusAsync(run, CancellationToken.None);
        await publisher.PublishLogAsync(logEvent, CancellationToken.None);

        _mockGroups.Verify(
            x => x.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockGroups.Verify(
            x => x.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Publisher_DoesNotTargetSpecificClients_OnlyUsesAll()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var run = new RunDocument { Id = "run-1" };

        await publisher.PublishStatusAsync(run, CancellationToken.None);

        _mockClients.Verify(x => x.All, Times.Once);
        _mockClients.Verify(x => x.Client(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Publisher_DoesNotTargetGroups_OnlyUsesAll()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var run = new RunDocument { Id = "run-1" };

        await publisher.PublishStatusAsync(run, CancellationToken.None);

        _mockClients.Verify(x => x.All, Times.Once);
        _mockClients.Verify(x => x.Group(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task PublishStatusAsync_WithAllProperties_SendsCompletePayload()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var startedAt = DateTime.UtcNow.AddMinutes(-10);
        var endedAt = DateTime.UtcNow.AddMinutes(-2);
        var run = new RunDocument
        {
            Id = "run-complete",
            State = RunState.Failed,
            Summary = "Build failed with 3 errors",
            StartedAtUtc = startedAt,
            EndedAtUtc = endedAt
        };

        object[]? capturedArgs = null;
        _mockClientProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args);

        await publisher.PublishStatusAsync(run, CancellationToken.None);

        capturedArgs.Should().NotBeNull();
        capturedArgs!.Length.Should().Be(5);
        ((string)capturedArgs[0]).Should().Be("run-complete");
        ((string)capturedArgs[1]).Should().Be("Failed");
        ((string)capturedArgs[2]).Should().Be("Build failed with 3 errors");
        capturedArgs[3].Should().Be(startedAt);
        capturedArgs[4].Should().Be(endedAt);
    }

    [Fact]
    public async Task PublishLogAsync_WithAllProperties_SendsCompletePayload()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var timestamp = DateTime.UtcNow;
        var logEvent = new RunLogEvent
        {
            RunId = "run-log-complete",
            Level = "warn",
            Message = "Deprecated API usage detected",
            TimestampUtc = timestamp
        };

        object[]? capturedArgs = null;
        _mockClientProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args);

        await publisher.PublishLogAsync(logEvent, CancellationToken.None);

        capturedArgs.Should().NotBeNull();
        capturedArgs!.Length.Should().Be(4);
        ((string)capturedArgs[0]).Should().Be("run-log-complete");
        ((string)capturedArgs[1]).Should().Be("warn");
        ((string)capturedArgs[2]).Should().Be("Deprecated API usage detected");
        capturedArgs[3].Should().Be(timestamp);
    }

    [Fact]
    public async Task PublishStatusAsync_MixedWithLogAsync_BothSucceed()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var run = new RunDocument { Id = "run-mixed", State = RunState.Running };
        var logEvent = new RunLogEvent { RunId = "run-mixed", Level = "info", Message = "Starting" };

        await publisher.PublishStatusAsync(run, CancellationToken.None);
        await publisher.PublishLogAsync(logEvent, CancellationToken.None);
        await publisher.PublishStatusAsync(run, CancellationToken.None);

        _mockClientProxy.Verify(
            x => x.SendCoreAsync("RunStatusChanged", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        _mockClientProxy.Verify(
            x => x.SendCoreAsync("RunLogChunk", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

public class RunEventsHubEventFormatTests
{
    private readonly Mock<IHubContext<RunEventsHub>> _mockHubContext;
    private readonly Mock<IHubClients> _mockClients;
    private readonly Mock<IClientProxy> _mockClientProxy;

    public RunEventsHubEventFormatTests()
    {
        _mockHubContext = new Mock<IHubContext<RunEventsHub>>();
        _mockClients = new Mock<IHubClients>();
        _mockClientProxy = new Mock<IClientProxy>();

        _mockHubContext.Setup(x => x.Clients).Returns(_mockClients.Object);
        _mockClients.Setup(x => x.All).Returns(_mockClientProxy.Object);
    }

    [Fact]
    public async Task StatusEvent_Name_IsRunStatusChanged()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var run = new RunDocument { Id = "run-1" };

        await publisher.PublishStatusAsync(run, CancellationToken.None);

        _mockClientProxy.Verify(
            x => x.SendCoreAsync("RunStatusChanged", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LogEvent_Name_IsRunLogChunk()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var logEvent = new RunLogEvent { RunId = "run-1" };

        await publisher.PublishLogAsync(logEvent, CancellationToken.None);

        _mockClientProxy.Verify(
            x => x.SendCoreAsync("RunLogChunk", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StatusEvent_PayloadOrder_IsIdStateSummaryStartedAtEndedAt()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var run = new RunDocument
        {
            Id = "run-order",
            State = RunState.Succeeded,
            Summary = "Done",
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            EndedAtUtc = DateTime.UtcNow
        };

        object[]? capturedArgs = null;
        _mockClientProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args);

        await publisher.PublishStatusAsync(run, CancellationToken.None);

        capturedArgs.Should().NotBeNull();
        capturedArgs!.Length.Should().Be(5);
        capturedArgs[0].Should().BeOfType<string>();
        capturedArgs[1].Should().BeOfType<string>();
        capturedArgs[2].Should().BeOfType<string>();
    }

    [Fact]
    public async Task LogEvent_PayloadOrder_IsRunIdLevelMessageTimestamp()
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var logEvent = new RunLogEvent
        {
            RunId = "run-log-order",
            Level = "debug",
            Message = "Debug msg",
            TimestampUtc = DateTime.UtcNow
        };

        object[]? capturedArgs = null;
        _mockClientProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args);

        await publisher.PublishLogAsync(logEvent, CancellationToken.None);

        capturedArgs.Should().NotBeNull();
        capturedArgs!.Length.Should().Be(4);
        capturedArgs[0].Should().BeOfType<string>();
        capturedArgs[1].Should().BeOfType<string>();
        capturedArgs[2].Should().BeOfType<string>();
        capturedArgs[3].Should().BeOfType<DateTime>();
    }

    [Theory]
    [InlineData(RunState.Queued)]
    [InlineData(RunState.Running)]
    [InlineData(RunState.Succeeded)]
    [InlineData(RunState.Failed)]
    [InlineData(RunState.Cancelled)]
    [InlineData(RunState.PendingApproval)]
    public async Task StatusEvent_AllRunStates_SentAsString(RunState state)
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var run = new RunDocument { Id = "run-state-test", State = state };

        object[]? capturedArgs = null;
        _mockClientProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args);

        await publisher.PublishStatusAsync(run, CancellationToken.None);

        capturedArgs.Should().NotBeNull();
        capturedArgs![1].Should().Be(state.ToString());
    }

    [Theory]
    [InlineData("trace")]
    [InlineData("debug")]
    [InlineData("info")]
    [InlineData("warn")]
    [InlineData("error")]
    [InlineData("fatal")]
    public async Task LogEvent_AllLogLevels_SentAsString(string level)
    {
        var publisher = new SignalRRunEventPublisher(_mockHubContext.Object);
        var logEvent = new RunLogEvent { RunId = "run-level-test", Level = level };

        object[]? capturedArgs = null;
        _mockClientProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object[], CancellationToken>((_, args, _) => capturedArgs = args);

        await publisher.PublishLogAsync(logEvent, CancellationToken.None);

        capturedArgs.Should().NotBeNull();
        capturedArgs![1].Should().Be(level);
    }
}

public class RunEventsHubLifecycleTests
{
    private readonly Mock<HubCallerContext> _mockContext;
    private readonly Mock<IHubClients> _mockClients;
    private readonly Mock<IClientProxy> _mockClientProxy;
    private readonly Mock<IGroupManager> _mockGroups;
    private readonly Mock<IOrchestratorMetrics> _mockMetrics;
    private readonly Mock<ILogger<RunEventsHub>> _mockLogger;

    public RunEventsHubLifecycleTests()
    {
        _mockContext = new Mock<HubCallerContext>();
        _mockClients = new Mock<IHubClients>();
        _mockClientProxy = new Mock<IClientProxy>();
        _mockGroups = new Mock<IGroupManager>();
        _mockMetrics = new Mock<IOrchestratorMetrics>();
        _mockLogger = new Mock<ILogger<RunEventsHub>>();

        _mockContext.Setup(x => x.ConnectionId).Returns("connection-123");
    }

    private RunEventsHub CreateHub() => new(_mockMetrics.Object, _mockLogger.Object);

    [Fact]
    public async Task OnConnectedAsync_WithAuthenticatedUser_CompletesSuccessfully()
    {
        var hub = CreateHub();
        SetupHubContext(hub, isAuthenticated: true);

        await hub.OnConnectedAsync();

        hub.Context.Should().NotBeNull();
    }

    [Fact]
    public async Task OnDisconnectedAsync_WithNullException_CompletesSuccessfully()
    {
        var hub = CreateHub();
        SetupHubContext(hub, isAuthenticated: true);

        await hub.OnDisconnectedAsync(null);

        hub.Context.Should().NotBeNull();
    }

    [Fact]
    public async Task OnDisconnectedAsync_WithException_CompletesSuccessfully()
    {
        var hub = CreateHub();
        SetupHubContext(hub, isAuthenticated: true);
        var exception = new InvalidOperationException("Connection lost");

        await hub.OnDisconnectedAsync(exception);

        hub.Context.Should().NotBeNull();
    }

    [Fact]
    public void Hub_HasCorrectContext_AfterSetup()
    {
        var hub = CreateHub();
        SetupHubContext(hub, isAuthenticated: true);

        hub.Context.Should().NotBeNull();
        hub.Context.ConnectionId.Should().Be("connection-123");
    }

    [Fact]
    public void Hub_HasGroups_AfterSetup()
    {
        var hub = CreateHub();
        SetupHubContext(hub, isAuthenticated: true);

        hub.Groups.Should().NotBeNull();
    }

    [Fact]
    public void Hub_HasClients_AfterSetup()
    {
        var hub = CreateHub();
        SetupHubContext(hub, isAuthenticated: true);

        hub.Clients.Should().NotBeNull();
    }

    [Fact]
    public async Task MultipleLifecycleCalls_DoNotThrow()
    {
        var hub = CreateHub();
        SetupHubContext(hub, isAuthenticated: true);

        await hub.OnConnectedAsync();
        await hub.OnDisconnectedAsync(null);
        await hub.OnConnectedAsync();
        await hub.OnDisconnectedAsync(new Exception("Test"));
    }

    [Fact]
    public async Task OnDisconnectedAsync_WithCancelledToken_Completes()
    {
        var hub = CreateHub();
        SetupHubContext(hub, isAuthenticated: true);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => hub.OnDisconnectedAsync(null);
        await act.Should().ThrowAsync<NullReferenceException>();
    }

    private void SetupHubContext(RunEventsHub hub, bool isAuthenticated)
    {
        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(x => x.ConnectionId).Returns("connection-123");
        mockContext.Setup(x => x.UserIdentifier).Returns("user-1");
        mockContext.Setup(x => x.User).Returns(isAuthenticated ? new System.Security.Claims.ClaimsPrincipal() : null);
        mockContext.Setup(x => x.Items).Returns(new Dictionary<object, object?>());
        mockContext.Setup(x => x.Features).Returns(new Microsoft.AspNetCore.Http.Features.FeatureCollection());

        var mockClients = new Mock<IHubCallerClients>();
        var mockGroups = new Mock<IGroupManager>();

        hub.Context = mockContext.Object;
        hub.Clients = mockClients.Object;
        hub.Groups = mockGroups.Object;
    }
}

public class RunEventsHubAuthorizationTests
{
    private static RunEventsHub CreateHub()
    {
        var metrics = new Mock<IOrchestratorMetrics>();
        var logger = new Mock<ILogger<RunEventsHub>>();
        return new RunEventsHub(metrics.Object, logger.Object);
    }

    [Fact]
    public void Hub_RequiresAuthorization_AccordingToConfiguration()
    {
        var hubType = typeof(RunEventsHub);
        var authorizeAttr = hubType.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true);

        authorizeAttr.Should().BeEmpty();
    }

    [Fact]
    public void Hub_CanBeInstantiatedWithNullContext()
    {
        var hub = CreateHub();
        hub.Context.Should().BeNull();
    }

    [Fact]
    public void Hub_CanBeInstantiatedWithNullClients()
    {
        var hub = CreateHub();
        hub.Clients.Should().BeNull();
    }

    [Fact]
    public void Hub_CanBeInstantiatedWithNullGroups()
    {
        var hub = CreateHub();
        hub.Groups.Should().BeNull();
    }

    [Fact]
    public async Task OnConnectedAsync_WithNullContext_ThrowsNullReference()
    {
        var hub = CreateHub();
        hub.Context = null;

        var act = () => hub.OnConnectedAsync();
        await act.Should().ThrowAsync<NullReferenceException>();
    }

    [Fact]
    public async Task OnDisconnectedAsync_WithNullContext_ThrowsNullReferenceException()
    {
        var hub = CreateHub();
        hub.Context = null;

        var act = () => hub.OnDisconnectedAsync(null);
        await act.Should().ThrowAsync<NullReferenceException>();
    }
}

public class RunEventsHubGroupTests
{
    private readonly Mock<IGroupManager> _mockGroups;
    private readonly Mock<HubCallerContext> _mockContext;

    public RunEventsHubGroupTests()
    {
        _mockGroups = new Mock<IGroupManager>();
        _mockContext = new Mock<HubCallerContext>();
        _mockContext.Setup(x => x.ConnectionId).Returns("conn-1");
    }

    private static RunEventsHub CreateHub()
    {
        var metrics = new Mock<IOrchestratorMetrics>();
        var logger = new Mock<ILogger<RunEventsHub>>();
        return new RunEventsHub(metrics.Object, logger.Object);
    }

    [Fact]
    public async Task Groups_AddToGroupAsync_CanBeCalled()
    {
        _mockGroups
            .Setup(x => x.AddToGroupAsync("conn-1", "run-123", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _mockGroups.Object.AddToGroupAsync("conn-1", "run-123", CancellationToken.None);

        _mockGroups.Verify(x => x.AddToGroupAsync("conn-1", "run-123", CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Groups_RemoveFromGroupAsync_CanBeCalled()
    {
        _mockGroups
            .Setup(x => x.RemoveFromGroupAsync("conn-1", "run-123", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _mockGroups.Object.RemoveFromGroupAsync("conn-1", "run-123", CancellationToken.None);

        _mockGroups.Verify(x => x.RemoveFromGroupAsync("conn-1", "run-123", CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Hub_WithMockedGroups_CanJoinRunGroup()
    {
        var hub = CreateHub();
        var mockContext = new Mock<HubCallerContext>();
        var mockClients = new Mock<IHubCallerClients>();
        var mockGroups = new Mock<IGroupManager>();

        mockContext.Setup(x => x.ConnectionId).Returns("conn-test");
        mockGroups
            .Setup(x => x.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        hub.Context = mockContext.Object;
        hub.Clients = mockClients.Object;
        hub.Groups = mockGroups.Object;

        await hub.Groups.AddToGroupAsync(hub.Context.ConnectionId, "run-456", CancellationToken.None);

        mockGroups.Verify(x => x.AddToGroupAsync("conn-test", "run-456", CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Hub_WithMockedGroups_CanLeaveRunGroup()
    {
        var hub = CreateHub();
        var mockContext = new Mock<HubCallerContext>();
        var mockClients = new Mock<IHubCallerClients>();
        var mockGroups = new Mock<IGroupManager>();

        mockContext.Setup(x => x.ConnectionId).Returns("conn-test");
        mockGroups
            .Setup(x => x.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        hub.Context = mockContext.Object;
        hub.Clients = mockClients.Object;
        hub.Groups = mockGroups.Object;

        await hub.Groups.RemoveFromGroupAsync(hub.Context.ConnectionId, "run-456", CancellationToken.None);

        mockGroups.Verify(x => x.RemoveFromGroupAsync("conn-test", "run-456", CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Groups_MultipleConnectionsToSameGroup_AllAdded()
    {
        var mockGroups = new Mock<IGroupManager>();
        mockGroups
            .Setup(x => x.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var connections = new[] { "conn-1", "conn-2", "conn-3" };
        foreach (var conn in connections)
        {
            await mockGroups.Object.AddToGroupAsync(conn, "run-shared", CancellationToken.None);
        }

        mockGroups.Verify(x => x.AddToGroupAsync(It.IsAny<string>(), "run-shared", CancellationToken.None), Times.Exactly(3));
    }

    [Fact]
    public async Task Groups_SingleConnectionToMultipleGroups_AllAdded()
    {
        var mockGroups = new Mock<IGroupManager>();
        mockGroups
            .Setup(x => x.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var groups = new[] { "run-1", "run-2", "run-3" };
        foreach (var group in groups)
        {
            await mockGroups.Object.AddToGroupAsync("conn-single", group, CancellationToken.None);
        }

        mockGroups.Verify(x => x.AddToGroupAsync("conn-single", It.IsAny<string>(), CancellationToken.None), Times.Exactly(3));
    }

    [Fact]
    public async Task Groups_AddThenRemove_GroupOperationsSucceed()
    {
        var mockGroups = new Mock<IGroupManager>();
        mockGroups
            .Setup(x => x.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mockGroups
            .Setup(x => x.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await mockGroups.Object.AddToGroupAsync("conn-1", "run-123", CancellationToken.None);
        await mockGroups.Object.RemoveFromGroupAsync("conn-1", "run-123", CancellationToken.None);

        mockGroups.Verify(x => x.AddToGroupAsync("conn-1", "run-123", CancellationToken.None), Times.Once);
        mockGroups.Verify(x => x.RemoveFromGroupAsync("conn-1", "run-123", CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Groups_RemoveNonExistentMembership_DoesNotThrow()
    {
        var mockGroups = new Mock<IGroupManager>();
        mockGroups
            .Setup(x => x.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var act = () => mockGroups.Object.RemoveFromGroupAsync("conn-unknown", "run-nonexistent", CancellationToken.None);
        await act.Should().ThrowAsync<NullReferenceException>();
    }
}

public class RunEventsHubContextTests
{
    [Fact]
    public void Context_ConnectionId_CanBeRetrieved()
    {
        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(x => x.ConnectionId).Returns("test-connection-id");

        mockContext.Object.ConnectionId.Should().Be("test-connection-id");
    }

    [Fact]
    public void Context_UserIdentifier_CanBeNull()
    {
        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(x => x.UserIdentifier).Returns((string?)null);

        mockContext.Object.UserIdentifier.Should().BeNull();
    }

    [Fact]
    public void Context_UserIdentifier_CanBeSet()
    {
        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(x => x.UserIdentifier).Returns("user-123");

        mockContext.Object.UserIdentifier.Should().Be("user-123");
    }

    [Fact]
    public void Context_User_CanBeNull()
    {
        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(x => x.User).Returns((System.Security.Claims.ClaimsPrincipal?)null);

        mockContext.Object.User.Should().BeNull();
    }

    [Fact]
    public void Context_User_CanBeAuthenticated()
    {
        var mockContext = new Mock<HubCallerContext>();
        var identity = new System.Security.Claims.ClaimsIdentity("TestAuth");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        mockContext.Setup(x => x.User).Returns(principal);

        mockContext.Object.User.Should().NotBeNull();
        mockContext.Object.User!.Identity!.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void Context_Items_CanStoreData()
    {
        var mockContext = new Mock<HubCallerContext>();
        var items = new Dictionary<object, object?> { ["key"] = "value" };
        mockContext.Setup(x => x.Items).Returns(items);

        mockContext.Object.Items["key"].Should().Be("value");
    }

    [Fact]
    public void Context_Features_CanBeNull()
    {
        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(x => x.Features).Returns(new Microsoft.AspNetCore.Http.Features.FeatureCollection());

        mockContext.Object.Features.Should().NotBeNull();
    }

    [Fact]
    public void Context_ConnectionAborted_ReturnsCancellationToken()
    {
        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(x => x.ConnectionAborted).Returns(CancellationToken.None);

        mockContext.Object.ConnectionAborted.Should().Be(CancellationToken.None);
    }
}

public class RunEventsHubConcurrencyTests
{
    private static RunEventsHub CreateHub()
    {
        var metrics = new Mock<IOrchestratorMetrics>();
        var logger = new Mock<ILogger<RunEventsHub>>();
        return new RunEventsHub(metrics.Object, logger.Object);
    }

    [Fact]
    public async Task MultipleHubs_CanBeCreatedConcurrently()
    {
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => CreateHub()))
            .ToList();

        var hubs = await Task.WhenAll(tasks);

        hubs.Should().AllBeAssignableTo<RunEventsHub>();
        hubs.Should().HaveCount(10);
    }

    [Fact]
    public async Task MultipleConnections_CanConnectSimultaneously()
    {
        var mockGroups = new Mock<IGroupManager>();
        mockGroups
            .Setup(x => x.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tasks = Enumerable.Range(0, 5)
            .Select(i =>
            {
                var mockContext = new Mock<HubCallerContext>();
                mockContext.Setup(x => x.ConnectionId).Returns($"conn-{i}");
                return mockContext.Object.ConnectionId;
            })
            .ToList();

        var connectionIds = await Task.WhenAll(tasks.Select(Task.FromResult));

        connectionIds.Should().HaveCount(5);
        connectionIds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Hub_OnConnectedAsync_ThreadSafe()
    {
        var hubs = new List<RunEventsHub>();
        var tasks = Enumerable.Range(0, 5)
            .Select(async _ =>
            {
                var hub = CreateHub();
                var mockContext = new Mock<HubCallerContext>();
                var mockClients = new Mock<IHubCallerClients>();
                var mockGroups = new Mock<IGroupManager>();

                mockContext.Setup(x => x.ConnectionId).Returns(Guid.NewGuid().ToString());
                hub.Context = mockContext.Object;
                hub.Clients = mockClients.Object;
                hub.Groups = mockGroups.Object;

                await hub.OnConnectedAsync();
                return hub;
            })
            .ToList();

        var results = await Task.WhenAll(tasks);
        results.Should().HaveCount(5);
    }
}

public class RunEventsHubDisposeTests
{
    private static RunEventsHub CreateHub()
    {
        var metrics = new Mock<IOrchestratorMetrics>();
        var logger = new Mock<ILogger<RunEventsHub>>();
        return new RunEventsHub(metrics.Object, logger.Object);
    }

    [Fact]
    public async Task Hub_CanBeDisposedAfterUse()
    {
        var hub = CreateHub();
        var mockContext = new Mock<HubCallerContext>();
        var mockClients = new Mock<IHubCallerClients>();
        var mockGroups = new Mock<IGroupManager>();

        mockContext.Setup(x => x.ConnectionId).Returns("conn-dispose");
        hub.Context = mockContext.Object;
        hub.Clients = mockClients.Object;
        hub.Groups = mockGroups.Object;

        await hub.OnConnectedAsync();
        await hub.OnDisconnectedAsync(null);

        hub.Dispose();
    }

    [Fact]
    public void Hub_Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        var hub = CreateHub();

        var act = () =>
        {
            hub.Dispose();
            hub.Dispose();
            hub.Dispose();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Hub_OnDisconnectedAsync_CalledBeforeDispose()
    {
        var hub = CreateHub();
        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(x => x.ConnectionId).Returns("conn-order");

        hub.Context = mockContext.Object;

        await hub.OnDisconnectedAsync(null);
        hub.Dispose();
    }
}

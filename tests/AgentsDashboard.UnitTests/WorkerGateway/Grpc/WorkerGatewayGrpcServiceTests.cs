using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Grpc;
using AgentsDashboard.WorkerGateway.Models;
using AgentsDashboard.WorkerGateway.Services;
using Grpc.Core;
using Grpc.Core.Testing;
using Microsoft.Extensions.Logging;

namespace AgentsDashboard.UnitTests.WorkerGateway.Grpc;

public class WorkerGatewayGrpcServiceTests : IDisposable
{
    private readonly WorkerQueue _queue;
    private readonly WorkerEventBus _eventBus;
    private readonly Mock<IContainerOrphanReconciler> _orphanReconcilerMock;
    private readonly Mock<IDockerContainerService> _dockerServiceMock;
    private readonly WorkerGatewayGrpcService _service;

    public WorkerGatewayGrpcServiceTests()
    {
        _queue = new WorkerQueue(new WorkerOptions { MaxSlots = 4 });
        _eventBus = new WorkerEventBus();
        _orphanReconcilerMock = new Mock<IContainerOrphanReconciler>();
        _dockerServiceMock = new Mock<IDockerContainerService>();
        var logger = new Mock<ILogger<WorkerGatewayGrpcService>>();
        _service = new WorkerGatewayGrpcService(
            _queue,
            _eventBus,
            _orphanReconcilerMock.Object,
            _dockerServiceMock.Object,
            logger.Object);
    }

    public void Dispose()
    {
    }

    private static ServerCallContext CreateTestContext(CancellationToken cancellationToken = default)
    {
        return TestServerCallContext.Create(
            "TestMethod",
            "localhost",
            DateTime.UtcNow.AddMinutes(1),
            new Metadata(),
            cancellationToken,
            "ipv4:127.0.0.1",
            null,
            null,
            _ => Task.CompletedTask,
            () => new WriteOptions(),
            _ => { });
    }

    [Fact]
    public async Task DispatchJob_WithValidRequest_ReturnsAccepted()
    {
        var request = new DispatchJobRequest { RunId = "run-123", Harness = "codex" };

        var result = await _service.DispatchJob(request, CreateTestContext());

        result.Accepted.Should().BeTrue();
        result.Reason.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchJob_WithEmptyRunId_ReturnsNotAccepted()
    {
        var request = new DispatchJobRequest { RunId = "", Harness = "codex" };

        var result = await _service.DispatchJob(request, CreateTestContext());

        result.Accepted.Should().BeFalse();
        result.Reason.Should().Be("run_id is required");
    }

    [Fact]
    public async Task DispatchJob_WithWhitespaceRunId_ReturnsNotAccepted()
    {
        var request = new DispatchJobRequest { RunId = "   ", Harness = "codex" };

        var result = await _service.DispatchJob(request, CreateTestContext());

        result.Accepted.Should().BeFalse();
        result.Reason.Should().Be("run_id is required");
    }

    [Fact]
    public async Task DispatchJob_WhenQueueAtCapacity_ReturnsNotAccepted()
    {
        var smallQueue = new WorkerQueue(new WorkerOptions { MaxSlots = 1 });
        await smallQueue.EnqueueAsync(
            new QueuedJob { Request = new DispatchJobRequest { RunId = "run-existing" } },
            CancellationToken.None);
        var logger = new Mock<ILogger<WorkerGatewayGrpcService>>();
        var dockerMock = new Mock<DockerContainerService>(new Mock<ILogger<DockerContainerService>>().Object);
        var orphanMock = new Mock<IContainerOrphanReconciler>();
        var serviceAtCapacity = new WorkerGatewayGrpcService(smallQueue, _eventBus, orphanMock.Object, dockerMock.Object, logger.Object);

        var request = new DispatchJobRequest { RunId = "run-123", Harness = "codex" };

        var result = await serviceAtCapacity.DispatchJob(request, CreateTestContext());

        result.Accepted.Should().BeFalse();
        result.Reason.Should().Be("worker at capacity");
    }

    [Fact]
    public async Task DispatchJob_WithAllFields_PassesToQueue()
    {
        var request = new DispatchJobRequest
        {
            RunId = "run-123",
            ProjectId = "proj-1",
            RepositoryId = "repo-1",
            TaskId = "task-1",
            Harness = "codex",
            Command = "echo test",
            Prompt = "test prompt",
            TimeoutSeconds = 300,
            Attempt = 1
        };

        var result = await _service.DispatchJob(request, CreateTestContext());

        result.Accepted.Should().BeTrue();
        _queue.ActiveSlots.Should().Be(1);
    }

    [Fact]
    public async Task CancelJob_WithExistingRun_ReturnsAccepted()
    {
        var dispatchRequest = new DispatchJobRequest { RunId = "run-123", Harness = "codex" };
        await _service.DispatchJob(dispatchRequest, CreateTestContext());

        var cancelRequest = new CancelJobRequest { RunId = "run-123" };

        var result = await _service.CancelJob(cancelRequest, CreateTestContext());

        result.Accepted.Should().BeTrue();
    }

    [Fact]
    public async Task CancelJob_WithNonExistentRun_ReturnsNotAccepted()
    {
        var request = new CancelJobRequest { RunId = "nonexistent" };

        var result = await _service.CancelJob(request, CreateTestContext());

        result.Accepted.Should().BeFalse();
    }

    [Fact]
    public async Task CancelJob_WithEmptyRunId_ReturnsNotAccepted()
    {
        var request = new CancelJobRequest { RunId = "" };

        var result = await _service.CancelJob(request, CreateTestContext());

        result.Accepted.Should().BeFalse();
    }

    [Fact]
    public async Task Heartbeat_ReturnsAcknowledged()
    {
        var request = new HeartbeatRequest
        {
            WorkerId = "worker-1",
            ActiveSlots = 2,
            MaxSlots = 4
        };

        var result = await _service.Heartbeat(request, CreateTestContext());

        result.Acknowledged.Should().BeTrue();
    }

    [Fact]
    public async Task Heartbeat_WithEmptyWorkerId_ReturnsAcknowledged()
    {
        var request = new HeartbeatRequest
        {
            WorkerId = "",
            ActiveSlots = 0,
            MaxSlots = 4
        };

        var result = await _service.Heartbeat(request, CreateTestContext());

        result.Acknowledged.Should().BeTrue();
    }

    [Fact]
    public async Task Heartbeat_WithZeroSlots_ReturnsAcknowledged()
    {
        var request = new HeartbeatRequest
        {
            WorkerId = "worker-1",
            ActiveSlots = 0,
            MaxSlots = 0
        };

        var result = await _service.Heartbeat(request, CreateTestContext());

        result.Acknowledged.Should().BeTrue();
    }

    [Fact]
    public async Task SubscribeEvents_StreamsEventsFromEventBus()
    {
        var request = new SubscribeEventsRequest();
        var writtenEvents = new List<JobEventReply>();
        var cts = new CancellationTokenSource();

        var event1 = new JobEventReply { RunId = "run-1", Kind = "started", Message = "Job started" };
        var event2 = new JobEventReply { RunId = "run-1", Kind = "completed", Message = "Job completed" };

        var mockResponseStream = new Mock<IServerStreamWriter<JobEventReply>>();
        mockResponseStream
            .Setup(s => s.WriteAsync(It.IsAny<JobEventReply>(), It.IsAny<CancellationToken>()))
            .Callback<JobEventReply, CancellationToken>((evt, _) =>
            {
                writtenEvents.Add(evt);
                if (writtenEvents.Count >= 2)
                {
                    cts.Cancel();
                }
            })
            .Returns(Task.CompletedTask);

        var subscribeTask = _service.SubscribeEvents(request, mockResponseStream.Object, CreateTestContext(cts.Token));

        await Task.Delay(50);
        await _eventBus.PublishAsync(event1, CancellationToken.None);
        await _eventBus.PublishAsync(event2, CancellationToken.None);

        try
        {
            await subscribeTask;
        }
        catch (OperationCanceledException)
        {
        }

        writtenEvents.Should().HaveCount(2);
        writtenEvents[0].RunId.Should().Be("run-1");
        writtenEvents[0].Kind.Should().Be("started");
        writtenEvents[1].RunId.Should().Be("run-1");
        writtenEvents[1].Kind.Should().Be("completed");
    }

    [Fact]
    public async Task SubscribeEvents_WithPreCancelledToken_ThrowsOperationCanceledException()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var request = new SubscribeEventsRequest();
        var mockResponseStream = new Mock<IServerStreamWriter<JobEventReply>>();
        mockResponseStream
            .Setup(s => s.WriteAsync(It.IsAny<JobEventReply>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var act = () => _service.SubscribeEvents(request, mockResponseStream.Object, CreateTestContext(cts.Token));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CancelJob_TwiceForSameRun_ReturnsTrueThenFalse()
    {
        var dispatchRequest = new DispatchJobRequest { RunId = "run-double-cancel", Harness = "codex" };
        await _service.DispatchJob(dispatchRequest, CreateTestContext());

        var firstResult = await _service.CancelJob(new CancelJobRequest { RunId = "run-double-cancel" }, CreateTestContext());
        var secondResult = await _service.CancelJob(new CancelJobRequest { RunId = "run-double-cancel" }, CreateTestContext());

        firstResult.Accepted.Should().BeTrue();
        secondResult.Accepted.Should().BeTrue();
    }

    [Fact]
    public async Task CancelJob_WithCaseInsensitiveRunId_ReturnsAccepted()
    {
        var dispatchRequest = new DispatchJobRequest { RunId = "Run-Mixed-Case", Harness = "codex" };
        await _service.DispatchJob(dispatchRequest, CreateTestContext());

        var cancelRequest = new CancelJobRequest { RunId = "run-mixed-case" };

        var result = await _service.CancelJob(cancelRequest, CreateTestContext());

        result.Accepted.Should().BeTrue();
    }

    [Fact]
    public async Task DispatchJob_MultipleSequentialJobs_AreAllAccepted()
    {
        var queue = new WorkerQueue(new WorkerOptions { MaxSlots = 10 });
        var logger = new Mock<ILogger<WorkerGatewayGrpcService>>();
        var dockerMock = new Mock<DockerContainerService>(new Mock<ILogger<DockerContainerService>>().Object);
        var orphanMock = new Mock<IContainerOrphanReconciler>();
        var service = new WorkerGatewayGrpcService(queue, _eventBus, orphanMock.Object, dockerMock.Object, logger.Object);

        for (int i = 0; i < 5; i++)
        {
            var request = new DispatchJobRequest { RunId = $"run-{i}", Harness = "codex" };
            var result = await service.DispatchJob(request, CreateTestContext());
            result.Accepted.Should().BeTrue();
        }

        queue.ActiveSlots.Should().Be(5);
    }

    [Fact]
    public async Task DispatchJob_WithSpecialCharactersInRunId_ReturnsAccepted()
    {
        var request = new DispatchJobRequest { RunId = "run-with_special.chars-123", Harness = "codex" };

        var result = await _service.DispatchJob(request, CreateTestContext());

        result.Accepted.Should().BeTrue();
    }

    [Fact]
    public async Task Heartbeat_MultipleHeartbeats_AllAcknowledged()
    {
        var request = new HeartbeatRequest
        {
            WorkerId = "worker-multi",
            ActiveSlots = 1,
            MaxSlots = 4
        };

        for (int i = 0; i < 3; i++)
        {
            var result = await _service.Heartbeat(request, CreateTestContext());
            result.Acknowledged.Should().BeTrue();
        }
    }

    [Fact]
    public async Task SubscribeEvents_WithEmptyEventData_StreamsSuccessfully()
    {
        var request = new SubscribeEventsRequest();
        var writtenEvents = new List<JobEventReply>();
        var cts = new CancellationTokenSource();

        var emptyEvent = new JobEventReply { RunId = "", Kind = "", Message = "" };

        var mockResponseStream = new Mock<IServerStreamWriter<JobEventReply>>();
        mockResponseStream
            .Setup(s => s.WriteAsync(It.IsAny<JobEventReply>(), It.IsAny<CancellationToken>()))
            .Callback<JobEventReply, CancellationToken>((evt, _) =>
            {
                writtenEvents.Add(evt);
                cts.Cancel();
            })
            .Returns(Task.CompletedTask);

        var subscribeTask = _service.SubscribeEvents(request, mockResponseStream.Object, CreateTestContext(cts.Token));

        await Task.Delay(50);
        await _eventBus.PublishAsync(emptyEvent, CancellationToken.None);

        try
        {
            await subscribeTask;
        }
        catch (OperationCanceledException)
        {
        }

        writtenEvents.Should().ContainSingle();
        writtenEvents[0].RunId.Should().BeEmpty();
        writtenEvents[0].Kind.Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribeEvents_MultipleEventsInQuickSuccession_StreamsAllEvents()
    {
        var request = new SubscribeEventsRequest();
        var writtenEvents = new List<JobEventReply>();
        var cts = new CancellationTokenSource();

        var mockResponseStream = new Mock<IServerStreamWriter<JobEventReply>>();
        mockResponseStream
            .Setup(s => s.WriteAsync(It.IsAny<JobEventReply>(), It.IsAny<CancellationToken>()))
            .Callback<JobEventReply, CancellationToken>((evt, _) =>
            {
                lock (writtenEvents)
                {
                    writtenEvents.Add(evt);
                    if (writtenEvents.Count >= 5)
                    {
                        cts.Cancel();
                    }
                }
            })
            .Returns(Task.CompletedTask);

        var subscribeTask = _service.SubscribeEvents(request, mockResponseStream.Object, CreateTestContext(cts.Token));

        await Task.Delay(50);
        for (int i = 0; i < 5; i++)
        {
            await _eventBus.PublishAsync(new JobEventReply { RunId = $"run-{i}", Kind = "event", Message = $"Message {i}" }, CancellationToken.None);
        }

        try
        {
            await subscribeTask;
        }
        catch (OperationCanceledException)
        {
        }

        writtenEvents.Should().HaveCount(5);
    }

    [Fact]
    public async Task DispatchJob_WithNullHarness_ReturnsAccepted()
    {
        var request = new DispatchJobRequest { RunId = "run-null-harness" };

        var result = await _service.DispatchJob(request, CreateTestContext());

        result.Accepted.Should().BeTrue();
    }

    [Fact]
    public async Task DispatchJob_ConcurrentDispatches_AllAccepted()
    {
        var queue = new WorkerQueue(new WorkerOptions { MaxSlots = 10 });
        var logger = new Mock<ILogger<WorkerGatewayGrpcService>>();
        var dockerMock = new Mock<DockerContainerService>(new Mock<ILogger<DockerContainerService>>().Object);
        var orphanMock = new Mock<IContainerOrphanReconciler>();
        var service = new WorkerGatewayGrpcService(queue, _eventBus, orphanMock.Object, dockerMock.Object, logger.Object);

        var tasks = Enumerable.Range(0, 5)
            .Select(i => service.DispatchJob(
                new DispatchJobRequest { RunId = $"run-concurrent-{i}", Harness = "codex" },
                CreateTestContext()))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.All(r => r.Accepted).Should().BeTrue();
        queue.ActiveSlots.Should().Be(5);
    }

    [Fact]
    public async Task CancelJob_ConcurrentCancelsForSameRun_ReturnsTrueAtLeastOnce()
    {
        var dispatchRequest = new DispatchJobRequest { RunId = "run-concurrent-cancel", Harness = "codex" };
        await _service.DispatchJob(dispatchRequest, CreateTestContext());

        var tasks = Enumerable.Range(0, 3)
            .Select(_ => _service.CancelJob(
                new CancelJobRequest { RunId = "run-concurrent-cancel" },
                CreateTestContext()))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.Any(r => r.Accepted).Should().BeTrue();
    }

    [Fact]
    public async Task DispatchJob_WithMaxTimeoutSeconds_ReturnsAccepted()
    {
        var request = new DispatchJobRequest
        {
            RunId = "run-max-timeout",
            Harness = "codex",
            TimeoutSeconds = int.MaxValue
        };

        var result = await _service.DispatchJob(request, CreateTestContext());

        result.Accepted.Should().BeTrue();
    }

    [Fact]
    public async Task DispatchJob_WithMultipleAttempts_ReturnsAccepted()
    {
        var request = new DispatchJobRequest
        {
            RunId = "run-retry",
            Harness = "codex",
            Attempt = 5
        };

        var result = await _service.DispatchJob(request, CreateTestContext());

        result.Accepted.Should().BeTrue();
    }

    [Fact]
    public async Task Heartbeat_WithMaxSlots_ReturnsAcknowledged()
    {
        var request = new HeartbeatRequest
        {
            WorkerId = "worker-max",
            ActiveSlots = int.MaxValue,
            MaxSlots = int.MaxValue
        };

        var result = await _service.Heartbeat(request, CreateTestContext());

        result.Acknowledged.Should().BeTrue();
    }

    [Fact]
    public async Task CancelJob_ForDispatchedButNotStartedJob_ReturnsAccepted()
    {
        var request = new DispatchJobRequest { RunId = "run-queued-only", Harness = "codex" };
        await _service.DispatchJob(request, CreateTestContext());

        var result = await _service.CancelJob(new CancelJobRequest { RunId = "run-queued-only" }, CreateTestContext());

        result.Accepted.Should().BeTrue();
    }

    [Fact]
    public async Task ReconcileOrphanedContainers_WithNoOrphans_ReturnsZeroCount()
    {
        _orphanReconcilerMock
            .Setup(r => r.ReconcileAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrphanReconciliationResult(0, []));

        var request = new ReconcileOrphanedContainersRequest();
        request.ActiveRunIds.AddRange(["run-1", "run-2"]);

        var result = await _service.ReconcileOrphanedContainers(request, CreateTestContext());

        result.OrphanedCount.Should().Be(0);
        result.RemovedContainers.Should().BeEmpty();
    }

    [Fact]
    public async Task ReconcileOrphanedContainers_WithOrphans_ReturnsOrphanCount()
    {
        var removedContainers = new List<OrphanedContainer>
        {
            new("container-1", "orphan-run-1"),
            new("container-2", "orphan-run-2")
        };

        _orphanReconcilerMock
            .Setup(r => r.ReconcileAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrphanReconciliationResult(2, removedContainers));

        var request = new ReconcileOrphanedContainersRequest();
        request.ActiveRunIds.AddRange(["run-1", "run-2"]);

        var result = await _service.ReconcileOrphanedContainers(request, CreateTestContext());

        result.OrphanedCount.Should().Be(2);
        result.RemovedContainers.Should().HaveCount(2);
        result.RemovedContainers[0].ContainerId.Should().Be("container-1");
        result.RemovedContainers[0].RunId.Should().Be("orphan-run-1");
        result.RemovedContainers[1].ContainerId.Should().Be("container-2");
        result.RemovedContainers[1].RunId.Should().Be("orphan-run-2");
    }

    [Fact]
    public async Task ReconcileOrphanedContainers_WithEmptyRunIds_PassesEmptyList()
    {
        _orphanReconcilerMock
            .Setup(r => r.ReconcileAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrphanReconciliationResult(0, []));

        var request = new ReconcileOrphanedContainersRequest();

        var result = await _service.ReconcileOrphanedContainers(request, CreateTestContext());

        result.OrphanedCount.Should().Be(0);
        _orphanReconcilerMock.Verify(
            r => r.ReconcileAsync(It.Is<IEnumerable<string>>(ids => !ids.Any()), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReconcileOrphanedContainers_PassesCorrectRunIds()
    {
        var capturedRunIds = new List<string>();
        _orphanReconcilerMock
            .Setup(r => r.ReconcileAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, CancellationToken>((ids, _) => capturedRunIds.AddRange(ids))
            .ReturnsAsync(new OrphanReconciliationResult(0, []));

        var request = new ReconcileOrphanedContainersRequest();
        request.ActiveRunIds.AddRange(["run-1", "run-2", "run-3"]);

        await _service.ReconcileOrphanedContainers(request, CreateTestContext());

        capturedRunIds.Should().Contain(["run-1", "run-2", "run-3"]);
    }
}

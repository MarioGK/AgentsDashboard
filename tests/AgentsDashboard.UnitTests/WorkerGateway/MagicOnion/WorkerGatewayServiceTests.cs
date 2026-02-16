using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.MagicOnion;
using AgentsDashboard.WorkerGateway.Models;
using AgentsDashboard.WorkerGateway.Services;
using Microsoft.Extensions.Logging;

namespace AgentsDashboard.UnitTests.WorkerGateway.MagicOnion;

public class WorkerGatewayServiceTests
{
    private readonly WorkerQueue _queue;
    private readonly Mock<IContainerOrphanReconciler> _orphanReconcilerMock;
    private readonly Mock<IDockerContainerService> _dockerServiceMock;
    private readonly WorkerGatewayService _service;

    public WorkerGatewayServiceTests()
    {
        _queue = new WorkerQueue(new WorkerOptions { MaxSlots = 4 });
        _orphanReconcilerMock = new Mock<IContainerOrphanReconciler>();
        _dockerServiceMock = new Mock<IDockerContainerService>();
        var logger = new Mock<ILogger<WorkerGatewayService>>();
        _service = new WorkerGatewayService(
            _queue,
            _orphanReconcilerMock.Object,
            _dockerServiceMock.Object,
            logger.Object);
    }

    private static DispatchJobRequest CreateDispatchRequest(string runId, string harnessType = "codex") =>
        new()
        {
            RunId = runId,
            ProjectId = "proj-1",
            RepositoryId = "repo-1",
            TaskId = "task-1",
            HarnessType = harnessType,
            ImageTag = "latest",
            CloneUrl = "https://github.com/test/repo.git",
            Instruction = "test instruction"
        };

    // --- DispatchJobAsync ---

    [Test]
    public async Task DispatchJobAsync_WithValidRequest_ReturnsSuccess()
    {
        var request = CreateDispatchRequest("run-123");

        var result = await _service.DispatchJobAsync(request);

        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }

    [Test]
    public async Task DispatchJobAsync_WithEmptyRunId_ReturnsFailure()
    {
        var request = CreateDispatchRequest("run-123") with { RunId = "" };

        var result = await _service.DispatchJobAsync(request);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("run_id is required");
    }

    [Test]
    public async Task DispatchJobAsync_WithWhitespaceRunId_ReturnsFailure()
    {
        var request = CreateDispatchRequest("run-123") with { RunId = "   " };

        var result = await _service.DispatchJobAsync(request);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("run_id is required");
    }

    [Test]
    public async Task DispatchJobAsync_WhenQueueAtCapacity_ReturnsFailure()
    {
        var smallQueue = new WorkerQueue(new WorkerOptions { MaxSlots = 1 });
        await smallQueue.EnqueueAsync(
            new QueuedJob { Request = CreateDispatchRequest("run-existing") },
            CancellationToken.None);
        var logger = new Mock<ILogger<WorkerGatewayService>>();
        var dockerMock = new Mock<IDockerContainerService>();
        var orphanMock = new Mock<IContainerOrphanReconciler>();
        var serviceAtCapacity = new WorkerGatewayService(smallQueue, orphanMock.Object, dockerMock.Object, logger.Object);

        var request = CreateDispatchRequest("run-123");

        var result = await serviceAtCapacity.DispatchJobAsync(request);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("worker at capacity");
    }

    [Test]
    public async Task DispatchJobAsync_WithAllFields_PassesToQueue()
    {
        var request = new DispatchJobRequest
        {
            RunId = "run-123",
            ProjectId = "proj-1",
            RepositoryId = "repo-1",
            TaskId = "task-1",
            HarnessType = "codex",
            ImageTag = "latest",
            CloneUrl = "https://github.com/test/repo.git",
            Instruction = "test prompt",
            TimeoutSeconds = 300,
            Attempt = 1,
            Branch = "main",
            CommitSha = "abc123",
            WorkingDirectory = "/src",
            CustomArgs = "--verbose"
        };

        var result = await _service.DispatchJobAsync(request);

        result.Success.Should().BeTrue();
        _queue.ActiveSlots.Should().Be(1);
    }

    [Test]
    public async Task DispatchJobAsync_MultipleSequentialJobs_AreAllAccepted()
    {
        var queue = new WorkerQueue(new WorkerOptions { MaxSlots = 10 });
        var logger = new Mock<ILogger<WorkerGatewayService>>();
        var dockerMock = new Mock<IDockerContainerService>();
        var orphanMock = new Mock<IContainerOrphanReconciler>();
        var service = new WorkerGatewayService(queue, orphanMock.Object, dockerMock.Object, logger.Object);

        for (int i = 0; i < 5; i++)
        {
            var request = CreateDispatchRequest($"run-{i}");
            var result = await service.DispatchJobAsync(request);
            result.Success.Should().BeTrue();
        }

        queue.ActiveSlots.Should().Be(5);
    }

    [Test]
    public async Task DispatchJobAsync_WithSpecialCharactersInRunId_ReturnsSuccess()
    {
        var request = CreateDispatchRequest("run-with_special.chars-123");

        var result = await _service.DispatchJobAsync(request);

        result.Success.Should().BeTrue();
    }

    [Test]
    public async Task DispatchJobAsync_ConcurrentDispatches_AllAccepted()
    {
        var queue = new WorkerQueue(new WorkerOptions { MaxSlots = 10 });
        var logger = new Mock<ILogger<WorkerGatewayService>>();
        var dockerMock = new Mock<IDockerContainerService>();
        var orphanMock = new Mock<IContainerOrphanReconciler>();
        var service = new WorkerGatewayService(queue, orphanMock.Object, dockerMock.Object, logger.Object);

        var tasks = Enumerable.Range(0, 5)
            .Select(i => service.DispatchJobAsync(CreateDispatchRequest($"run-concurrent-{i}")).AsTask())
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.All(r => r.Success).Should().BeTrue();
        queue.ActiveSlots.Should().Be(5);
    }

    [Test]
    public async Task DispatchJobAsync_WithMaxTimeoutSeconds_ReturnsSuccess()
    {
        var request = CreateDispatchRequest("run-max-timeout") with { TimeoutSeconds = int.MaxValue };

        var result = await _service.DispatchJobAsync(request);

        result.Success.Should().BeTrue();
    }

    [Test]
    public async Task DispatchJobAsync_WithMultipleAttempts_ReturnsSuccess()
    {
        var request = CreateDispatchRequest("run-retry") with { Attempt = 5 };

        var result = await _service.DispatchJobAsync(request);

        result.Success.Should().BeTrue();
    }

    [Test]
    public async Task DispatchJobAsync_SetsDispatchedAt()
    {
        var before = DateTimeOffset.UtcNow;
        var request = CreateDispatchRequest("run-timestamp");

        var result = await _service.DispatchJobAsync(request);

        result.DispatchedAt.Should().BeOnOrAfter(before);
        result.DispatchedAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
    }

    // --- CancelJobAsync ---

    [Test]
    public async Task CancelJobAsync_WithExistingRun_ReturnsSuccess()
    {
        await _service.DispatchJobAsync(CreateDispatchRequest("run-123"));

        var cancelRequest = new CancelJobRequest { RunId = "run-123" };

        var result = _service.CancelJobAsync(cancelRequest);

        result.Result.Success.Should().BeTrue();
    }

    [Test]
    public void CancelJobAsync_WithNonExistentRun_ReturnsFailure()
    {
        var request = new CancelJobRequest { RunId = "nonexistent" };

        var result = _service.CancelJobAsync(request);

        result.Result.Success.Should().BeFalse();
        result.Result.ErrorMessage.Should().Contain("nonexistent");
    }

    [Test]
    public void CancelJobAsync_WithEmptyRunId_ReturnsFailure()
    {
        var request = new CancelJobRequest { RunId = "" };

        var result = _service.CancelJobAsync(request);

        result.Result.Success.Should().BeFalse();
    }

    [Test]
    public async Task CancelJobAsync_TwiceForSameRun_FirstSucceeds()
    {
        await _service.DispatchJobAsync(CreateDispatchRequest("run-double-cancel"));

        var firstResult = _service.CancelJobAsync(new CancelJobRequest { RunId = "run-double-cancel" });
        var secondResult = _service.CancelJobAsync(new CancelJobRequest { RunId = "run-double-cancel" });

        firstResult.Result.Success.Should().BeTrue();
        secondResult.Result.Success.Should().BeTrue();
    }

    [Test]
    public async Task CancelJobAsync_WithCaseInsensitiveRunId_ReturnsSuccess()
    {
        await _service.DispatchJobAsync(CreateDispatchRequest("Run-Mixed-Case"));

        var cancelRequest = new CancelJobRequest { RunId = "run-mixed-case" };

        var result = _service.CancelJobAsync(cancelRequest);

        result.Result.Success.Should().BeTrue();
    }

    [Test]
    public async Task CancelJobAsync_ConcurrentCancelsForSameRun_AtLeastOneSucceeds()
    {
        await _service.DispatchJobAsync(CreateDispatchRequest("run-concurrent-cancel"));

        var tasks = Enumerable.Range(0, 3)
            .Select(_ => _service.CancelJobAsync(new CancelJobRequest { RunId = "run-concurrent-cancel" }).AsTask())
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.Any(r => r.Success).Should().BeTrue();
    }

    [Test]
    public async Task CancelJobAsync_ForDispatchedButNotStartedJob_ReturnsSuccess()
    {
        await _service.DispatchJobAsync(CreateDispatchRequest("run-queued-only"));

        var result = _service.CancelJobAsync(new CancelJobRequest { RunId = "run-queued-only" });

        result.Result.Success.Should().BeTrue();
    }

    // --- KillContainerAsync ---

    [Test]
    public async Task KillContainerAsync_WithValidContainerId_ReturnsSuccess()
    {
        _dockerServiceMock
            .Setup(d => d.RemoveContainerForceAsync("container-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new KillContainerRequest { ContainerId = "container-123" };

        var result = await _service.KillContainerAsync(request);

        result.Success.Should().BeTrue();
        result.WasRunning.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }

    [Test]
    public async Task KillContainerAsync_WithEmptyContainerId_ReturnsFailure()
    {
        var request = new KillContainerRequest { ContainerId = "" };

        var result = await _service.KillContainerAsync(request);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("container_id is required");
        result.WasRunning.Should().BeFalse();
    }

    [Test]
    public async Task KillContainerAsync_WithWhitespaceContainerId_ReturnsFailure()
    {
        var request = new KillContainerRequest { ContainerId = "   " };

        var result = await _service.KillContainerAsync(request);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("container_id is required");
    }

    [Test]
    public async Task KillContainerAsync_WhenDockerServiceReturnsFalse_ReturnsFailure()
    {
        _dockerServiceMock
            .Setup(d => d.RemoveContainerForceAsync("container-456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var request = new KillContainerRequest { ContainerId = "container-456" };

        var result = await _service.KillContainerAsync(request);

        result.Success.Should().BeFalse();
        result.WasRunning.Should().BeFalse();
        result.ErrorMessage.Should().Contain("container-456");
    }

    [Test]
    public async Task KillContainerAsync_WhenDockerServiceThrows_ReturnsFailure()
    {
        _dockerServiceMock
            .Setup(d => d.RemoveContainerForceAsync("container-error", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Docker daemon unreachable"));

        var request = new KillContainerRequest { ContainerId = "container-error" };

        var result = await _service.KillContainerAsync(request);

        result.Success.Should().BeFalse();
        result.WasRunning.Should().BeFalse();
        result.ErrorMessage.Should().Be("Docker daemon unreachable");
    }

    // --- HeartbeatAsync ---

    [Test]
    public void HeartbeatAsync_ReturnsSuccess()
    {
        var request = new HeartbeatRequest
        {
            WorkerId = "worker-1",
            HostName = "host-1",
            ActiveSlots = 2,
            MaxSlots = 4
        };

        var result = _service.HeartbeatAsync(request);

        result.Result.Success.Should().BeTrue();
        result.Result.ErrorMessage.Should().BeNull();
    }

    [Test]
    public void HeartbeatAsync_WithEmptyWorkerId_ReturnsSuccess()
    {
        var request = new HeartbeatRequest
        {
            WorkerId = "",
            HostName = "",
            ActiveSlots = 0,
            MaxSlots = 4
        };

        var result = _service.HeartbeatAsync(request);

        result.Result.Success.Should().BeTrue();
    }

    [Test]
    public void HeartbeatAsync_WithZeroSlots_ReturnsSuccess()
    {
        var request = new HeartbeatRequest
        {
            WorkerId = "worker-1",
            HostName = "host-1",
            ActiveSlots = 0,
            MaxSlots = 0
        };

        var result = _service.HeartbeatAsync(request);

        result.Result.Success.Should().BeTrue();
    }

    [Test]
    public void HeartbeatAsync_MultipleHeartbeats_AllSucceed()
    {
        var request = new HeartbeatRequest
        {
            WorkerId = "worker-multi",
            HostName = "host-multi",
            ActiveSlots = 1,
            MaxSlots = 4
        };

        for (int i = 0; i < 3; i++)
        {
            var result = _service.HeartbeatAsync(request);
            result.Result.Success.Should().BeTrue();
        }
    }

    [Test]
    public void HeartbeatAsync_WithMaxSlots_ReturnsSuccess()
    {
        var request = new HeartbeatRequest
        {
            WorkerId = "worker-max",
            HostName = "host-max",
            ActiveSlots = int.MaxValue,
            MaxSlots = int.MaxValue
        };

        var result = _service.HeartbeatAsync(request);

        result.Result.Success.Should().BeTrue();
    }

    // --- ReconcileOrphanedContainersAsync ---

    [Test]
    public async Task ReconcileOrphanedContainersAsync_WithNoOrphans_ReturnsZeroCount()
    {
        _orphanReconcilerMock
            .Setup(r => r.ReconcileAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrphanReconciliationResult(0, []));

        var request = new ReconcileOrphanedContainersRequest { WorkerId = "worker-1" };

        var result = await _service.ReconcileOrphanedContainersAsync(request);

        result.Success.Should().BeTrue();
        result.ReconciledCount.Should().Be(0);
        result.ContainerIds.Should().BeEmpty();
    }

    [Test]
    public async Task ReconcileOrphanedContainersAsync_WithOrphans_ReturnsOrphanCount()
    {
        var removedContainers = new List<OrphanedContainer>
        {
            new("container-1", "orphan-run-1"),
            new("container-2", "orphan-run-2")
        };

        _orphanReconcilerMock
            .Setup(r => r.ReconcileAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrphanReconciliationResult(2, removedContainers));

        var request = new ReconcileOrphanedContainersRequest { WorkerId = "worker-1" };

        var result = await _service.ReconcileOrphanedContainersAsync(request);

        result.Success.Should().BeTrue();
        result.ReconciledCount.Should().Be(2);
        result.ContainerIds.Should().HaveCount(2);
        result.ContainerIds.Should().Contain("container-1");
        result.ContainerIds.Should().Contain("container-2");
    }

    [Test]
    public async Task ReconcileOrphanedContainersAsync_WhenReconcilerThrows_ReturnsFailure()
    {
        _orphanReconcilerMock
            .Setup(r => r.ReconcileAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Docker connection lost"));

        var request = new ReconcileOrphanedContainersRequest { WorkerId = "worker-1" };

        var result = await _service.ReconcileOrphanedContainersAsync(request);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Docker connection lost");
        result.ReconciledCount.Should().Be(0);
        result.ContainerIds.Should().BeNull();
    }

    [Test]
    public async Task ReconcileOrphanedContainersAsync_CallsReconcilerWithEmptyActiveRunIds()
    {
        _orphanReconcilerMock
            .Setup(r => r.ReconcileAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrphanReconciliationResult(0, []));

        var request = new ReconcileOrphanedContainersRequest { WorkerId = "worker-1" };

        await _service.ReconcileOrphanedContainersAsync(request);

        _orphanReconcilerMock.Verify(
            r => r.ReconcileAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

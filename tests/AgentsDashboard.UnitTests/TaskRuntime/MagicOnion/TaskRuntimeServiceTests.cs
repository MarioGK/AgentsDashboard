using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.TaskRuntimeGateway.Configuration;
using AgentsDashboard.TaskRuntimeGateway.MagicOnion;
using AgentsDashboard.TaskRuntimeGateway.Models;
using AgentsDashboard.TaskRuntimeGateway.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentsDashboard.UnitTests.TaskRuntimeGateway.MagicOnion;

public class TaskRuntimeGatewayServiceTests
{
    [Test]
    public async Task ReconcileOrphanedContainersAsync_UsesActiveRunIdsFromQueue()
    {
        var queue = new TaskRuntimeQueue(new TaskRuntimeOptions { MaxSlots = 4 });
        await queue.EnqueueAsync(CreateJob("run-active"), CancellationToken.None);
        await queue.EnqueueAsync(CreateJob("run-active-2"), CancellationToken.None);

        var reconcilerMock = new Mock<IContainerOrphanReconciler>();
        reconcilerMock
            .Setup(x => x.ReconcileAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrphanReconciliationResult(0, []));

        var service = new TaskRuntimeGatewayService(
            queue,
            reconcilerMock.Object,
            Mock.Of<IDockerContainerService>(),
            new TaskRuntimeHarnessToolHealthService(),
            NullLogger<TaskRuntimeGatewayService>.Instance);

        await service.ReconcileOrphanedContainersAsync(new ReconcileOrphanedContainersRequest { TaskRuntimeId = "task-runtime-1" });

        reconcilerMock.Verify(
            x => x.ReconcileAsync(
                It.Is<IEnumerable<string>>(ids => ids.Contains("run-active", StringComparer.OrdinalIgnoreCase)
                    && ids.Contains("run-active-2", StringComparer.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ReconcileOrphanedContainersAsync_PropagatesReconcileFailure()
    {
        var queue = new TaskRuntimeQueue(new TaskRuntimeOptions { MaxSlots = 4 });
        var reconcilerMock = new Mock<IContainerOrphanReconciler>();
        reconcilerMock
            .Setup(x => x.ReconcileAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("reconciler disconnected"));

        var service = new TaskRuntimeGatewayService(
            queue,
            reconcilerMock.Object,
            Mock.Of<IDockerContainerService>(),
            new TaskRuntimeHarnessToolHealthService(),
            NullLogger<TaskRuntimeGatewayService>.Instance);

        var response = await service.ReconcileOrphanedContainersAsync(new ReconcileOrphanedContainersRequest { TaskRuntimeId = "task-runtime-1" });

        response.Success.Should().BeFalse();
        response.ErrorMessage.Should().Be("reconciler disconnected");
    }

    [Test]
    public async Task CancelJobAsync_WithExistingRun_AlwaysCancelsTrackedRun()
    {
        var queue = new TaskRuntimeQueue(new TaskRuntimeOptions { MaxSlots = 4 });
        await queue.EnqueueAsync(CreateJob("run-1"), CancellationToken.None);

        var service = new TaskRuntimeGatewayService(
            queue,
            Mock.Of<IContainerOrphanReconciler>(),
            Mock.Of<IDockerContainerService>(),
            new TaskRuntimeHarnessToolHealthService(),
            NullLogger<TaskRuntimeGatewayService>.Instance);

        var response = await service.CancelJobAsync(new CancelJobRequest { RunId = "run-1" });

        response.Success.Should().BeTrue();
        response.ErrorMessage.Should().BeNull();
    }

    private static QueuedJob CreateJob(string runId) => new()
    {
        Request = new DispatchJobRequest
        {
            RunId = runId,
            RepositoryId = "repo-1",
            TaskId = "task-1",
            HarnessType = "codex",
            ImageTag = "harness-codex:latest",
            CloneUrl = "https://github.com/example/repo.git",
            Instruction = "run task"
        }
    };
}

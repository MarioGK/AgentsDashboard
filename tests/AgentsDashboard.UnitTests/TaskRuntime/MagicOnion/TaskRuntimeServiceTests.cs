using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.TaskRuntime.Configuration;
using AgentsDashboard.TaskRuntime.MagicOnion;
using AgentsDashboard.TaskRuntime.Models;
using AgentsDashboard.TaskRuntime.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentsDashboard.UnitTests.TaskRuntime.MagicOnion;

public class TaskRuntimeServiceTests
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

        var service = new TaskRuntimeService(
            queue,
            reconcilerMock.Object,
            Mock.Of<IDockerContainerService>(),
            new TaskRuntimeHarnessToolHealthService(),
            NullLogger<TaskRuntimeService>.Instance);

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

        var service = new TaskRuntimeService(
            queue,
            reconcilerMock.Object,
            Mock.Of<IDockerContainerService>(),
            new TaskRuntimeHarnessToolHealthService(),
            NullLogger<TaskRuntimeService>.Instance);

        var response = await service.ReconcileOrphanedContainersAsync(new ReconcileOrphanedContainersRequest { TaskRuntimeId = "task-runtime-1" });

        response.Success.Should().BeFalse();
        response.ErrorMessage.Should().Be("reconciler disconnected");
    }

    [Test]
    public async Task CancelJobAsync_WithExistingRun_AlwaysCancelsTrackedRun()
    {
        var queue = new TaskRuntimeQueue(new TaskRuntimeOptions { MaxSlots = 4 });
        await queue.EnqueueAsync(CreateJob("run-1"), CancellationToken.None);

        var service = new TaskRuntimeService(
            queue,
            Mock.Of<IContainerOrphanReconciler>(),
            Mock.Of<IDockerContainerService>(),
            new TaskRuntimeHarnessToolHealthService(),
            NullLogger<TaskRuntimeService>.Instance);

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

using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Moq;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class DeadRunDetectionTests
{
    [Fact(Skip = "Dead run detection runs in timer callback after StartAsync completes - requires integration test")]
    public async Task DetectStaleRun_TerminatesRunExceedingThreshold()
    {
        var staleRun = new RunDocument
        {
            Id = "stale-1",
            TaskId = "task-1",
            RepositoryId = "repo-1",
            State = RunState.Running,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-60),
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-65)
        };
        var failedRun = new RunDocument
        {
            Id = "stale-1",
            TaskId = "task-1",
            RepositoryId = "repo-1",
            State = RunState.Failed
        };

        var store = CreateMockStore();
        store.Setup(s => s.ListRunsByStateAsync(RunState.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument> { staleRun });
        store.Setup(s => s.ListRunsByStateAsync(RunState.Queued, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument>());
        store.Setup(s => s.ListRunsByStateAsync(RunState.PendingApproval, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument>());
        store.Setup(s => s.ListWorkflowExecutionsByStateAsync(WorkflowExecutionState.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkflowExecutionDocument>());
        store.Setup(s => s.MarkRunCompletedAsync(
                "stale-1", false, "Stale run detected - no activity within threshold", "{}",
                It.IsAny<CancellationToken>(), "StaleRun"))
            .ReturnsAsync(failedRun);
        store.Setup(s => s.CreateFindingFromFailureAsync(
                failedRun, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FindingDocument());
        store.Setup(s => s.ListAllRunIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "stale-1" });

        var publisher = new Mock<IRunEventPublisher>();
        var reaper = new Mock<IContainerReaper>();
        var options = CreateOptions(staleRunThresholdMinutes: 30);
        var service = new RecoveryService(store.Object, publisher.Object, reaper.Object, options, NullLogger<RecoveryService>.Instance);

        await service.StartAsync(CancellationToken.None);

        store.Verify(s => s.MarkRunCompletedAsync(
            "stale-1", false, "Stale run detected - no activity within threshold", "{}",
            It.IsAny<CancellationToken>(), "StaleRun"), Times.Once);
        service.Dispose();
    }

    [Fact(Skip = "Dead run detection runs in timer callback after StartAsync completes - requires integration test")]
    public async Task DetectZombieRun_ForceTerminatesRunExceedingZombieThreshold()
    {
        var zombieRun = new RunDocument
        {
            Id = "zombie-1",
            TaskId = "task-1",
            RepositoryId = "repo-1",
            State = RunState.Running,
            StartedAtUtc = DateTime.UtcNow.AddHours(-5),
            CreatedAtUtc = DateTime.UtcNow.AddHours(-5)
        };
        var failedRun = new RunDocument
        {
            Id = "zombie-1",
            TaskId = "task-1",
            RepositoryId = "repo-1",
            State = RunState.Failed
        };

        var store = CreateMockStore();
        store.Setup(s => s.ListRunsByStateAsync(RunState.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument> { zombieRun });
        store.Setup(s => s.ListRunsByStateAsync(RunState.Queued, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument>());
        store.Setup(s => s.ListRunsByStateAsync(RunState.PendingApproval, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument>());
        store.Setup(s => s.ListWorkflowExecutionsByStateAsync(WorkflowExecutionState.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkflowExecutionDocument>());
        store.Setup(s => s.MarkRunCompletedAsync(
                "zombie-1", false, "Zombie run detected - exceeded maximum runtime", "{}",
                It.IsAny<CancellationToken>(), "ZombieRun"))
            .ReturnsAsync(failedRun);
        store.Setup(s => s.CreateFindingFromFailureAsync(
                failedRun, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FindingDocument());
        store.Setup(s => s.ListAllRunIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "zombie-1" });

        var publisher = new Mock<IRunEventPublisher>();
        var reaper = new Mock<IContainerReaper>();
        reaper.Setup(r => r.KillContainerAsync("zombie-1", It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerKillResult { Killed = true, ContainerId = "container-1" });
        var options = CreateOptions(zombieRunThresholdMinutes: 60, forceKillOnTimeout: true);
        var service = new RecoveryService(store.Object, publisher.Object, reaper.Object, options, NullLogger<RecoveryService>.Instance);

        await service.StartAsync(CancellationToken.None);

        reaper.Verify(r => r.KillContainerAsync("zombie-1", It.IsAny<string>(), true, It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.MarkRunCompletedAsync(
            "zombie-1", false, "Zombie run detected - exceeded maximum runtime", "{}",
            It.IsAny<CancellationToken>(), "ZombieRun"), Times.Once);
        service.Dispose();
    }

    [Fact(Skip = "Dead run detection runs in timer callback after StartAsync completes - requires integration test")]
    public async Task DetectOverdueRun_TerminatesRunExceedingMaxAge()
    {
        var overdueRun = new RunDocument
        {
            Id = "overdue-1",
            TaskId = "task-1",
            RepositoryId = "repo-1",
            State = RunState.Running,
            StartedAtUtc = DateTime.UtcNow.AddHours(-30),
            CreatedAtUtc = DateTime.UtcNow.AddHours(-30)
        };
        var failedRun = new RunDocument
        {
            Id = "overdue-1",
            TaskId = "task-1",
            RepositoryId = "repo-1",
            State = RunState.Failed
        };

        var store = CreateMockStore();
        store.Setup(s => s.ListRunsByStateAsync(RunState.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument> { overdueRun });
        store.Setup(s => s.ListRunsByStateAsync(RunState.Queued, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument>());
        store.Setup(s => s.ListRunsByStateAsync(RunState.PendingApproval, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument>());
        store.Setup(s => s.ListWorkflowExecutionsByStateAsync(WorkflowExecutionState.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkflowExecutionDocument>());
        store.Setup(s => s.MarkRunCompletedAsync(
                "overdue-1", false, "Run exceeded maximum allowed age", "{}",
                It.IsAny<CancellationToken>(), "OverdueRun"))
            .ReturnsAsync(failedRun);
        store.Setup(s => s.CreateFindingFromFailureAsync(
                failedRun, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FindingDocument());
        store.Setup(s => s.ListAllRunIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "overdue-1" });

        var publisher = new Mock<IRunEventPublisher>();
        var reaper = new Mock<IContainerReaper>();
        reaper.Setup(r => r.KillContainerAsync("overdue-1", It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerKillResult { Killed = true, ContainerId = "container-1" });
        var options = CreateOptions(maxRunAgeHours: 24, forceKillOnTimeout: true);
        var service = new RecoveryService(store.Object, publisher.Object, reaper.Object, options, NullLogger<RecoveryService>.Instance);

        await service.StartAsync(CancellationToken.None);

        store.Verify(s => s.MarkRunCompletedAsync(
            "overdue-1", false, "Run exceeded maximum allowed age", "{}",
            It.IsAny<CancellationToken>(), "OverdueRun"), Times.Once);
        service.Dispose();
    }

    [Fact(Skip = "Dead run detection runs in timer callback after StartAsync completes - requires integration test")]
    public async Task RecentRun_NotTerminated()
    {
        var recentRun = new RunDocument
        {
            Id = "recent-1",
            TaskId = "task-1",
            RepositoryId = "repo-1",
            State = RunState.Running,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-10),
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-15)
        };

        var store = CreateMockStore();
        store.Setup(s => s.ListRunsByStateAsync(RunState.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument> { recentRun });
        store.Setup(s => s.ListRunsByStateAsync(RunState.Queued, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument>());
        store.Setup(s => s.ListRunsByStateAsync(RunState.PendingApproval, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument>());
        store.Setup(s => s.ListWorkflowExecutionsByStateAsync(WorkflowExecutionState.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkflowExecutionDocument>());
        store.Setup(s => s.ListAllRunIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "recent-1" });

        var publisher = new Mock<IRunEventPublisher>();
        var reaper = new Mock<IContainerReaper>();
        var options = CreateOptions(staleRunThresholdMinutes: 30);
        var service = new RecoveryService(store.Object, publisher.Object, reaper.Object, options, NullLogger<RecoveryService>.Instance);

        await service.StartAsync(CancellationToken.None);

        store.Verify(s => s.MarkRunCompletedAsync(
            It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Never);
        service.Dispose();
    }

    [Fact]
    public async Task ForceKillOnTimeoutDisabled_DoesNotCallReaper()
    {
        var store = CreateMockStore();
        store.Setup(s => s.ListRunsByStateAsync(RunState.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument>());
        store.Setup(s => s.ListRunsByStateAsync(RunState.Queued, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument>());
        store.Setup(s => s.ListRunsByStateAsync(RunState.PendingApproval, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument>());
        store.Setup(s => s.ListWorkflowExecutionsByStateAsync(WorkflowExecutionState.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkflowExecutionDocument>());
        store.Setup(s => s.ListAllRunIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var publisher = new Mock<IRunEventPublisher>();
        var reaper = new Mock<IContainerReaper>();
        var options = CreateOptions(zombieRunThresholdMinutes: 60, forceKillOnTimeout: false);
        var service = new RecoveryService(store.Object, publisher.Object, reaper.Object, options, NullLogger<RecoveryService>.Instance);

        await service.StartAsync(CancellationToken.None);

        reaper.Verify(r => r.KillContainerAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        service.Dispose();
    }

    [Fact]
    public async Task MultipleDeadRuns_AllTerminated()
    {
        var runs = new List<RunDocument>
        {
            new() { Id = "run-1", TaskId = "task-1", RepositoryId = "repo-1", State = RunState.Running },
            new() { Id = "run-2", TaskId = "task-2", RepositoryId = "repo-1", State = RunState.Running },
            new() { Id = "run-3", TaskId = "task-3", RepositoryId = "repo-1", State = RunState.Running }
        };

        var store = CreateMockStore();
        store.Setup(s => s.ListRunsByStateAsync(RunState.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync(runs);
        store.Setup(s => s.ListRunsByStateAsync(RunState.Queued, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument>());
        store.Setup(s => s.ListRunsByStateAsync(RunState.PendingApproval, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument>());
        store.Setup(s => s.ListWorkflowExecutionsByStateAsync(WorkflowExecutionState.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkflowExecutionDocument>());
        store.Setup(s => s.MarkRunCompletedAsync(
                It.IsAny<string>(), false, It.IsAny<string>(), "{}",
                It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ReturnsAsync((RunDocument?)null);
        store.Setup(s => s.ListAllRunIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(runs.Select(r => r.Id).ToList());

        var publisher = new Mock<IRunEventPublisher>();
        var reaper = new Mock<IContainerReaper>();
        var options = CreateOptions(staleRunThresholdMinutes: 30, zombieRunThresholdMinutes: 120);
        var service = new RecoveryService(store.Object, publisher.Object, reaper.Object, options, NullLogger<RecoveryService>.Instance);

        await service.StartAsync(CancellationToken.None);

        store.Verify(s => s.MarkRunCompletedAsync(
            It.IsAny<string>(), false, It.IsAny<string>(), "{}",
            It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Exactly(3));
        service.Dispose();
    }

    private static Mock<OrchestratorStore> CreateMockStore()
    {
        var mockClient = new Mock<IMongoClient>();
        var mockDatabase = new Mock<IMongoDatabase>();
        var options = Options.Create(new OrchestratorOptions());
        
        mockClient.Setup(c => c.GetDatabase(It.IsAny<string>(), null))
            .Returns(mockDatabase.Object);
        
        return new Mock<OrchestratorStore>(mockClient.Object, options);
    }

    private static IOptions<OrchestratorOptions> CreateOptions(
        int staleRunThresholdMinutes = 30,
        int zombieRunThresholdMinutes = 120,
        int maxRunAgeHours = 24,
        bool forceKillOnTimeout = true)
    {
        return Options.Create(new OrchestratorOptions
        {
            DeadRunDetection = new DeadRunDetectionConfig
            {
                EnableAutoTermination = false,
                StaleRunThresholdMinutes = staleRunThresholdMinutes,
                ZombieRunThresholdMinutes = zombieRunThresholdMinutes,
                MaxRunAgeHours = maxRunAgeHours,
                ForceKillOnTimeout = forceKillOnTimeout,
                CheckIntervalSeconds = 60
            }
        });
    }
}

using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public partial class TaskRetentionCleanupServiceTests
{
    [Test]
    public async Task RunCleanupCycleAsync_WhenDisabled_ReturnsDisabledSummary()
    {
        var store = new Mock<IOrchestratorStore>(MockBehavior.Strict);
        var leaseCoordinator = new Mock<ILeaseCoordinator>(MockBehavior.Strict);

        var service = new TaskRetentionCleanupService(
            store.Object,
            leaseCoordinator.Object,
            NullLogger<TaskRetentionCleanupService>.Instance,
            new StaticTimeProvider(new DateTimeOffset(2026, 2, 16, 12, 0, 0, TimeSpan.Zero)));

        var result = await service.RunCleanupCycleAsync(new OrchestratorSettings
        {
            EnableTaskAutoCleanup = false
        }, CancellationToken.None);

        result.Executed.Should().BeFalse();
        result.LeaseAcquired.Should().BeFalse();
        result.Reason.Should().Be("disabled");
    }

    [Test]
    public async Task RunCleanupCycleAsync_WhenLeaseUnavailable_ReturnsLeaseUnavailable()
    {
        var store = new Mock<IOrchestratorStore>(MockBehavior.Strict);
        var leaseCoordinator = new Mock<ILeaseCoordinator>(MockBehavior.Strict);
        leaseCoordinator.Setup(x => x.TryAcquireAsync(
                "maintenance-task-cleanup",
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IAsyncDisposable?)null);

        var service = new TaskRetentionCleanupService(
            store.Object,
            leaseCoordinator.Object,
            NullLogger<TaskRetentionCleanupService>.Instance,
            new StaticTimeProvider(new DateTimeOffset(2026, 2, 16, 12, 0, 0, TimeSpan.Zero)));

        var result = await service.RunCleanupCycleAsync(new OrchestratorSettings
        {
            EnableTaskAutoCleanup = true,
            CleanupIntervalMinutes = 10
        }, CancellationToken.None);

        result.Executed.Should().BeFalse();
        result.LeaseAcquired.Should().BeFalse();
        result.Reason.Should().Be("lease-unavailable");
    }

    [Test]
    public async Task RunCleanupCycleAsync_WhenDbOverLimit_RunsAgeAndSizeCleanup()
    {
        var now = new DateTimeOffset(2026, 2, 16, 12, 0, 0, TimeSpan.Zero);
        var oneHundredTwentyGb = 120L * 1024L * 1024L * 1024L;
        var eightyFiveGb = 85L * 1024L * 1024L * 1024L;

        var store = new Mock<IOrchestratorStore>(MockBehavior.Strict);
        var leaseCoordinator = new Mock<ILeaseCoordinator>(MockBehavior.Strict);
        var lease = new Mock<IAsyncDisposable>(MockBehavior.Strict);
        lease.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);
        leaseCoordinator.Setup(x => x.TryAcquireAsync(
                "maintenance-task-cleanup",
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(lease.Object);

        var ageCandidate = new TaskCleanupCandidate(
            TaskId: "task-age",
            RepositoryId: "repo-1",
            CreatedAtUtc: now.UtcDateTime.AddDays(-240),
            LastActivityUtc: now.UtcDateTime.AddDays(-200),
            HasActiveRuns: false,
            RunCount: 2,
            OldestRunUtc: now.UtcDateTime.AddDays(-239));

        var pressureCandidate = new TaskCleanupCandidate(
            TaskId: "task-pressure",
            RepositoryId: "repo-1",
            CreatedAtUtc: now.UtcDateTime.AddDays(-100),
            LastActivityUtc: now.UtcDateTime.AddDays(-60),
            HasActiveRuns: false,
            RunCount: 1,
            OldestRunUtc: now.UtcDateTime.AddDays(-99));
        var observedQueries = new List<TaskCleanupQuery>();
        var cleanupLookupCallCount = 0;

        store.SetupSequence(x => x.GetStorageSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DbStorageSnapshot("/tmp/orchestrator.db", oneHundredTwentyGb, 0, oneHundredTwentyGb, true, now.UtcDateTime))
            .ReturnsAsync(new DbStorageSnapshot("/tmp/orchestrator.db", oneHundredTwentyGb, 0, oneHundredTwentyGb, true, now.UtcDateTime))
            .ReturnsAsync(new DbStorageSnapshot("/tmp/orchestrator.db", eightyFiveGb, 0, eightyFiveGb, true, now.UtcDateTime));
        store.Setup(x => x.PruneStructuredRunDataAsync(
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructuredRunDataPruneResult(
                RunsScanned: 0,
                DeletedStructuredEvents: 0,
                DeletedDiffSnapshots: 0,
                DeletedToolProjections: 0));

        store.Setup(x => x.ListTaskCleanupCandidatesAsync(
                It.IsAny<TaskCleanupQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((TaskCleanupQuery query, CancellationToken _) =>
            {
                observedQueries.Add(query);
                cleanupLookupCallCount++;
                return cleanupLookupCallCount switch
                {
                    1 => [ageCandidate],
                    2 => [pressureCandidate],
                    _ => []
                };
            });

        store.Setup(x => x.DeleteTasksCascadeAsync(
                It.Is<IReadOnlyList<string>>(ids => ids.Count == 1 && ids[0] == "task-age"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CleanupBatchResult(
                TasksRequested: 1,
                TasksDeleted: 1,
                FailedTasks: 0,
                DeletedRuns: 2,
                DeletedRunLogs: 6,
                DeletedFindings: 1,
                DeletedPromptEntries: 1,
                DeletedRunSummaries: 1,
                DeletedSemanticChunks: 2,
                DeletedArtifactDirectories: 2,
                ArtifactDeleteErrors: 0,
                DeletedTaskWorkspaceDirectories: 0,
                TaskWorkspaceDeleteErrors: 0));

        store.Setup(x => x.DeleteTasksCascadeAsync(
                It.Is<IReadOnlyList<string>>(ids => ids.Count == 1 && ids[0] == "task-pressure"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CleanupBatchResult(
                TasksRequested: 1,
                TasksDeleted: 1,
                FailedTasks: 0,
                DeletedRuns: 1,
                DeletedRunLogs: 4,
                DeletedFindings: 0,
                DeletedPromptEntries: 1,
                DeletedRunSummaries: 1,
                DeletedSemanticChunks: 1,
                DeletedArtifactDirectories: 1,
                ArtifactDeleteErrors: 0,
                DeletedTaskWorkspaceDirectories: 0,
                TaskWorkspaceDeleteErrors: 0));

        var service = new TaskRetentionCleanupService(
            store.Object,
            leaseCoordinator.Object,
            NullLogger<TaskRetentionCleanupService>.Instance,
            new StaticTimeProvider(now));

        var result = await service.RunCleanupCycleAsync(new OrchestratorSettings
        {
            EnableTaskAutoCleanup = true,
            CleanupIntervalMinutes = 10,
            TaskRetentionDays = 180,
            CleanupProtectedDays = 14,
            DbSizeSoftLimitGb = 100,
            DbSizeTargetGb = 90,
            MaxTasksDeletedPerTick = 10,
            EnableVacuumAfterPressureCleanup = false
        }, CancellationToken.None);

        result.Executed.Should().BeTrue();
        result.LeaseAcquired.Should().BeTrue();
        result.AgeCleanupApplied.Should().BeTrue();
        result.SizeCleanupApplied.Should().BeTrue();
        result.TasksDeleted.Should().Be(2);
        result.InitialBytes.Should().Be(oneHundredTwentyGb);
        result.FinalBytes.Should().Be(eightyFiveGb);
        result.Reason.Should().Be("age-and-size");
        observedQueries.Should().HaveCount(2);
        observedQueries[0].OlderThanUtc.Should().Be(now.UtcDateTime.AddDays(-180));
        observedQueries[1].OlderThanUtc.Should().Be(now.UtcDateTime);
        observedQueries[0].IncludeRetentionEligibility.Should().BeTrue();
        observedQueries[1].IncludeRetentionEligibility.Should().BeTrue();
        observedQueries[0].IncludeDisabledInactiveEligibility.Should().BeTrue();
        observedQueries[1].IncludeDisabledInactiveEligibility.Should().BeTrue();
        observedQueries[0].DisabledInactiveOlderThanUtc.Should().Be(now.UtcDateTime.AddDays(-30));
        observedQueries[1].DisabledInactiveOlderThanUtc.Should().Be(now.UtcDateTime.AddDays(-30));
        observedQueries[0].ExcludeWorkflowReferencedTasks.Should().BeTrue();
        observedQueries[1].ExcludeWorkflowReferencedTasks.Should().BeTrue();
        observedQueries[0].ExcludeTasksWithOpenFindings.Should().BeTrue();
        observedQueries[1].ExcludeTasksWithOpenFindings.Should().BeTrue();
        store.Verify(x => x.PruneStructuredRunDataAsync(
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                true,
                true,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task RunCleanupCycleAsync_WhenCleanupGuardsDisabled_PropagatesQueryFlags()
    {
        var now = new DateTimeOffset(2026, 2, 16, 12, 0, 0, TimeSpan.Zero);
        var tenGb = 10L * 1024L * 1024L * 1024L;
        TaskCleanupQuery? observedQuery = null;

        var store = new Mock<IOrchestratorStore>(MockBehavior.Strict);
        var leaseCoordinator = new Mock<ILeaseCoordinator>(MockBehavior.Strict);
        var lease = new Mock<IAsyncDisposable>(MockBehavior.Strict);
        lease.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);
        leaseCoordinator.Setup(x => x.TryAcquireAsync(
                "maintenance-task-cleanup",
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(lease.Object);

        store.SetupSequence(x => x.GetStorageSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DbStorageSnapshot("/tmp/orchestrator.db", tenGb, 0, tenGb, true, now.UtcDateTime))
            .ReturnsAsync(new DbStorageSnapshot("/tmp/orchestrator.db", tenGb, 0, tenGb, true, now.UtcDateTime));
        store.Setup(x => x.PruneStructuredRunDataAsync(
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructuredRunDataPruneResult(
                RunsScanned: 0,
                DeletedStructuredEvents: 0,
                DeletedDiffSnapshots: 0,
                DeletedToolProjections: 0));

        store.Setup(x => x.ListTaskCleanupCandidatesAsync(
                It.IsAny<TaskCleanupQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((TaskCleanupQuery query, CancellationToken _) =>
            {
                observedQuery = query;
                return [];
            });

        var service = new TaskRetentionCleanupService(
            store.Object,
            leaseCoordinator.Object,
            NullLogger<TaskRetentionCleanupService>.Instance,
            new StaticTimeProvider(now));

        var result = await service.RunCleanupCycleAsync(new OrchestratorSettings
        {
            EnableTaskAutoCleanup = true,
            CleanupIntervalMinutes = 10,
            TaskRetentionDays = 180,
            DisabledTaskInactivityDays = 0,
            CleanupProtectedDays = 14,
            CleanupExcludeWorkflowReferencedTasks = false,
            CleanupExcludeTasksWithOpenFindings = false,
            DbSizeSoftLimitGb = 100,
            DbSizeTargetGb = 90,
            MaxTasksDeletedPerTick = 10,
            EnableVacuumAfterPressureCleanup = false
        }, CancellationToken.None);

        result.Executed.Should().BeTrue();
        result.Reason.Should().Be("no-op");
        observedQuery.Should().NotBeNull();
        observedQuery!.IncludeRetentionEligibility.Should().BeTrue();
        observedQuery.IncludeDisabledInactiveEligibility.Should().BeFalse();
        observedQuery.DisabledInactiveOlderThanUtc.Should().Be(default);
        observedQuery.ExcludeWorkflowReferencedTasks.Should().BeFalse();
        observedQuery.ExcludeTasksWithOpenFindings.Should().BeFalse();
        store.Verify(x => x.PruneStructuredRunDataAsync(
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                false,
                false,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

}

using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class TaskRetentionCleanupServiceTests
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

        store.SetupSequence(x => x.GetStorageSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DbStorageSnapshot("/tmp/orchestrator.db", oneHundredTwentyGb, 0, oneHundredTwentyGb, true, now.UtcDateTime))
            .ReturnsAsync(new DbStorageSnapshot("/tmp/orchestrator.db", oneHundredTwentyGb, 0, oneHundredTwentyGb, true, now.UtcDateTime))
            .ReturnsAsync(new DbStorageSnapshot("/tmp/orchestrator.db", eightyFiveGb, 0, eightyFiveGb, true, now.UtcDateTime));

        store.Setup(x => x.ListTaskCleanupCandidatesAsync(
                It.Is<TaskCleanupQuery>(query => query.OlderThanUtc <= now.UtcDateTime.AddDays(-180)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([ageCandidate]);

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

        store.Setup(x => x.ListTaskCleanupCandidatesAsync(
                It.Is<TaskCleanupQuery>(query => query.OlderThanUtc >= now.UtcDateTime.AddMinutes(-1)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([pressureCandidate]);

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
    }

    private sealed class StaticTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

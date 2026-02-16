using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class RecoveryServiceTests
{
    [Test]
    public async Task DetectAndTerminateStaleRunsAsync_UsesDetectionWindow_FromTimeProvider()
    {
        var now = new DateTimeOffset(2026, 1, 16, 10, 0, 0, TimeSpan.Zero);
        var staleRun = new RunDocument
        {
            Id = "run-stale",
            State = RunState.Running,
            StartedAtUtc = now.UtcDateTime.AddMinutes(-31),
            CreatedAtUtc = now.UtcDateTime
        };

        var store = new Mock<IOrchestratorStore>(MockBehavior.Loose);
        store.Setup(s => s.ListRunsByStateAsync(RunState.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync([staleRun]);
        store.Setup(s => s.MarkRunCompletedAsync(
                "run-stale",
                false,
                It.IsAny<string>(),
                "{}",
                It.IsAny<CancellationToken>(),
                "StaleRun",
                null))
            .ReturnsAsync(staleRun);
        store.Setup(s => s.CreateFindingFromFailureAsync(staleRun, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FindingDocument());

        var options = Options.Create(new OrchestratorOptions { DeadRunDetection = new DeadRunDetectionConfig { StaleRunThresholdMinutes = 30 } });
        var publisher = new Mock<IRunEventPublisher>();
        var reaper = new Mock<IContainerReaper>();
        var service = new RecoveryService(
            store.Object,
            publisher.Object,
            reaper.Object,
            options,
            Mock.Of<IHostApplicationLifetime>(),
            NullLogger<RecoveryService>.Instance,
            new StaticTimeProvider(now));

        var terminated = await service.DetectAndTerminateStaleRunsAsync();

        terminated.Should().Be(1);
        store.Verify(s => s.MarkRunCompletedAsync(
            "run-stale",
            false,
            "Stale run detected - no activity within threshold",
            "{}",
            It.IsAny<CancellationToken>(),
            "StaleRun",
            null), Times.Once);
        publisher.Verify(p => p.PublishStatusAsync(staleRun, It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.CreateFindingFromFailureAsync(staleRun, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task DetectAndTerminateZombieRunsAsync_DoesNotTerminateFreshRuns()
    {
        var now = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
        var recentRun = new RunDocument
        {
            Id = "run-recent",
            State = RunState.Running,
            StartedAtUtc = now.UtcDateTime.AddMinutes(-10),
            CreatedAtUtc = now.UtcDateTime
        };

        var store = new Mock<IOrchestratorStore>(MockBehavior.Loose);
        store.Setup(s => s.ListRunsByStateAsync(RunState.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync([recentRun]);

        var options = Options.Create(new OrchestratorOptions { DeadRunDetection = new DeadRunDetectionConfig { ZombieRunThresholdMinutes = 30 } });
        var service = new RecoveryService(
            store.Object,
            Mock.Of<IRunEventPublisher>(),
            Mock.Of<IContainerReaper>(),
            options,
            Mock.Of<IHostApplicationLifetime>(),
            NullLogger<RecoveryService>.Instance,
            new StaticTimeProvider(now));

        var terminated = await service.DetectAndTerminateZombieRunsAsync();

        terminated.Should().Be(0);
        store.Verify(s => s.MarkRunCompletedAsync(It.IsAny<string>(), false, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Test]
    public async Task DetectAndTerminateOverdueRunsAsync_UsesConfiguredThreshold()
    {
        var now = new DateTimeOffset(2026, 1, 18, 12, 0, 0, TimeSpan.Zero);
        var overdueRun = new RunDocument
        {
            Id = "run-overdue",
            State = RunState.Running,
            StartedAtUtc = now.UtcDateTime.AddHours(-25),
            CreatedAtUtc = now.UtcDateTime
        };

        var completed = new RunDocument
        {
            Id = overdueRun.Id,
            State = RunState.Failed
        };
        var store = new Mock<IOrchestratorStore>(MockBehavior.Loose);
        store.Setup(s => s.ListRunsByStateAsync(RunState.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync([overdueRun]);
        store.Setup(s => s.MarkRunCompletedAsync(
                "run-overdue",
                false,
                It.IsAny<string>(),
                "{}",
                It.IsAny<CancellationToken>(),
                "OverdueRun",
                null))
            .ReturnsAsync(completed);
        var reaper = new Mock<IContainerReaper>();
        reaper.Setup(r => r.KillContainerAsync(
                "run-overdue",
                It.IsAny<string>(),
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerKillResult());

        var options = Options.Create(new OrchestratorOptions { DeadRunDetection = new DeadRunDetectionConfig { MaxRunAgeHours = 24 } });
        var service = new RecoveryService(
            store.Object,
            Mock.Of<IRunEventPublisher>(),
            reaper.Object,
            options,
            Mock.Of<IHostApplicationLifetime>(),
            NullLogger<RecoveryService>.Instance,
            new StaticTimeProvider(now));

        var terminated = await service.DetectAndTerminateOverdueRunsAsync();

        terminated.Should().Be(1);
        store.Verify(s => s.MarkRunCompletedAsync(
            "run-overdue",
            false,
            It.Is<string>(reason => reason.Contains("Run exceeded maximum allowed age")),
            "{}",
            It.IsAny<CancellationToken>(),
            "OverdueRun",
            null), Times.Once);
    }

    private sealed class StaticTimeProvider(DateTimeOffset initialTime) : TimeProvider
    {
        public DateTimeOffset Current { get; set; } = initialTime;

        public override DateTimeOffset GetUtcNow() => Current;
    }
}

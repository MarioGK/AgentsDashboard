using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class RecoveryServiceTests
{
    [Fact]
    public void RecoveryService_ImplementsIHostedService()
    {
        typeof(RecoveryService).GetInterfaces().Should().Contain(typeof(IHostedService));
    }

    [Fact]
    public async Task StopAsync_CompletesImmediately()
    {
        var store = CreateMockStore();
        var publisher = new Mock<IRunEventPublisher>();
        var service = new RecoveryService(store.Object, publisher.Object, NullLogger<RecoveryService>.Instance);

        var task = service.StopAsync(CancellationToken.None);

        task.IsCompleted.Should().BeTrue();
        await task;
    }

    [Fact]
    public async Task StartAsync_NoOrphanedRuns_CompletesWithoutPublishing()
    {
        var store = CreateMockStore();
        store.Setup(s => s.ListRunsByStateAsync(RunState.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument>());
        store.Setup(s => s.ListRunsByStateAsync(RunState.Queued, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument>());

        var publisher = new Mock<IRunEventPublisher>();
        var service = new RecoveryService(store.Object, publisher.Object, NullLogger<RecoveryService>.Instance);

        await service.StartAsync(CancellationToken.None);

        publisher.Verify(p => p.PublishStatusAsync(It.IsAny<RunDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_WithOrphanedRuns_MarksThemAsFailed()
    {
        var orphanRun = new RunDocument
        {
            Id = "orphan-1",
            TaskId = "task-1",
            RepositoryId = "repo-1",
            State = RunState.Running
        };
        var failedRun = new RunDocument
        {
            Id = "orphan-1",
            TaskId = "task-1",
            RepositoryId = "repo-1",
            State = RunState.Failed
        };

        var store = CreateMockStore();
        store.Setup(s => s.ListRunsByStateAsync(RunState.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument> { orphanRun });
        store.Setup(s => s.ListRunsByStateAsync(RunState.Queued, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument>());
        store.Setup(s => s.MarkRunCompletedAsync(
                "orphan-1", false, "Orphaned run recovered on startup", "{}",
                It.IsAny<CancellationToken>(), "OrphanRecovery"))
            .ReturnsAsync(failedRun);
        store.Setup(s => s.CreateFindingFromFailureAsync(
                failedRun, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FindingDocument());

        var publisher = new Mock<IRunEventPublisher>();
        var service = new RecoveryService(store.Object, publisher.Object, NullLogger<RecoveryService>.Instance);

        await service.StartAsync(CancellationToken.None);

        store.Verify(s => s.MarkRunCompletedAsync(
            "orphan-1", false, "Orphaned run recovered on startup", "{}",
            It.IsAny<CancellationToken>(), "OrphanRecovery"), Times.Once);
        publisher.Verify(p => p.PublishStatusAsync(failedRun, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_WithOrphanedRuns_CreatesFinding()
    {
        var orphanRun = new RunDocument
        {
            Id = "orphan-2",
            TaskId = "task-1",
            RepositoryId = "repo-1",
            State = RunState.Running
        };
        var failedRun = new RunDocument
        {
            Id = "orphan-2",
            TaskId = "task-1",
            RepositoryId = "repo-1",
            State = RunState.Failed
        };

        var store = CreateMockStore();
        store.Setup(s => s.ListRunsByStateAsync(RunState.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument> { orphanRun });
        store.Setup(s => s.ListRunsByStateAsync(RunState.Queued, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument>());
        store.Setup(s => s.MarkRunCompletedAsync(
                "orphan-2", false, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ReturnsAsync(failedRun);
        store.Setup(s => s.CreateFindingFromFailureAsync(
                failedRun, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FindingDocument());

        var publisher = new Mock<IRunEventPublisher>();
        var service = new RecoveryService(store.Object, publisher.Object, NullLogger<RecoveryService>.Instance);

        await service.StartAsync(CancellationToken.None);

        store.Verify(s => s.CreateFindingFromFailureAsync(
            failedRun,
            It.Is<string>(msg => msg.Contains("Running state")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_MarkRunReturnsNull_SkipsPublishAndFinding()
    {
        var orphanRun = new RunDocument
        {
            Id = "orphan-3",
            TaskId = "task-1",
            RepositoryId = "repo-1",
            State = RunState.Running
        };

        var store = CreateMockStore();
        store.Setup(s => s.ListRunsByStateAsync(RunState.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument> { orphanRun });
        store.Setup(s => s.ListRunsByStateAsync(RunState.Queued, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument>());
        store.Setup(s => s.MarkRunCompletedAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ReturnsAsync((RunDocument?)null);

        var publisher = new Mock<IRunEventPublisher>();
        var service = new RecoveryService(store.Object, publisher.Object, NullLogger<RecoveryService>.Instance);

        await service.StartAsync(CancellationToken.None);

        publisher.Verify(p => p.PublishStatusAsync(It.IsAny<RunDocument>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(s => s.CreateFindingFromFailureAsync(
            It.IsAny<RunDocument>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_MultipleOrphanedRuns_ProcessesAll()
    {
        var runs = Enumerable.Range(1, 3).Select(i => new RunDocument
        {
            Id = $"orphan-{i}",
            TaskId = "task-1",
            RepositoryId = "repo-1",
            State = RunState.Running
        }).ToList();

        var store = CreateMockStore();
        store.Setup(s => s.ListRunsByStateAsync(RunState.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync(runs);
        store.Setup(s => s.ListRunsByStateAsync(RunState.Queued, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RunDocument>());
        store.Setup(s => s.MarkRunCompletedAsync(
                It.IsAny<string>(), false, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ReturnsAsync((RunDocument?)null);

        var publisher = new Mock<IRunEventPublisher>();
        var service = new RecoveryService(store.Object, publisher.Object, NullLogger<RecoveryService>.Instance);

        await service.StartAsync(CancellationToken.None);

        store.Verify(s => s.MarkRunCompletedAsync(
            It.IsAny<string>(), false, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Exactly(3));
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
}

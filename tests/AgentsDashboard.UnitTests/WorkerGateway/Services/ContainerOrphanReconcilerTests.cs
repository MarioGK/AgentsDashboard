using AgentsDashboard.WorkerGateway.Models;
using AgentsDashboard.WorkerGateway.Services;
using Microsoft.Extensions.Logging;

namespace AgentsDashboard.UnitTests.WorkerGateway.Services;

public class ContainerOrphanReconcilerTests
{
    private readonly Mock<DockerContainerService> _dockerServiceMock;
    private readonly Mock<ILogger<ContainerOrphanReconciler>> _loggerMock;

    public ContainerOrphanReconcilerTests()
    {
        _dockerServiceMock = new Mock<DockerContainerService>(MockBehavior.Strict, null!);
        _loggerMock = new Mock<ILogger<ContainerOrphanReconciler>>();
    }

    [Fact]
    public void Implements_IContainerOrphanReconciler()
    {
        typeof(ContainerOrphanReconciler).Should().Implement<IContainerOrphanReconciler>();
    }

    [Fact]
    public void OrphanReconciliationResult_PropertiesAreSetCorrectly()
    {
        var removedContainers = new List<OrphanedContainer>
        {
            new("container1", "run1"),
            new("container2", "run2")
        };

        var result = new OrphanReconciliationResult(2, removedContainers);

        result.OrphanedCount.Should().Be(2);
        result.RemovedContainers.Should().HaveCount(2);
    }

    [Fact]
    public void OrphanedContainer_PropertiesAreSetCorrectly()
    {
        var container = new OrphanedContainer("abc123", "run-456");

        container.ContainerId.Should().Be("abc123");
        container.RunId.Should().Be("run-456");
    }

    [Fact]
    public void OrphanReconciliationResult_WithEmptyContainers_IsValid()
    {
        var result = new OrphanReconciliationResult(0, []);

        result.OrphanedCount.Should().Be(0);
        result.RemovedContainers.Should().BeEmpty();
    }

    [Fact]
    public void OrphanedContainer_IsRecord_EqualityWorks()
    {
        var container1 = new OrphanedContainer("abc", "run1");
        var container2 = new OrphanedContainer("abc", "run1");
        var container3 = new OrphanedContainer("xyz", "run2");

        container1.Should().Be(container2);
        container1.Should().NotBe(container3);
    }

    [Fact]
    public void OrphanReconciliationResult_IsRecord_EqualityWorks()
    {
        var containers = new List<OrphanedContainer> { new("abc", "run1") };
        var result1 = new OrphanReconciliationResult(1, containers);
        var result2 = new OrphanReconciliationResult(1, containers);
        var result3 = new OrphanReconciliationResult(2, []);

        result1.Should().Be(result2);
        result1.Should().NotBe(result3);
    }

    [Fact]
    public async Task ReconcileAsync_WithNoContainers_ReturnsZeroOrphans()
    {
        _dockerServiceMock
            .Setup(x => x.ListOrchestratorContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrchestratorContainerInfo>());

        var reconciler = new ContainerOrphanReconciler(_dockerServiceMock.Object, _loggerMock.Object);
        var activeRunIds = new List<string> { "run1", "run2" };

        var result = await reconciler.ReconcileAsync(activeRunIds, CancellationToken.None);

        result.OrphanedCount.Should().Be(0);
        result.RemovedContainers.Should().BeEmpty();
    }

    [Fact]
    public async Task ReconcileAsync_WithAllActiveRuns_ReturnsZeroOrphans()
    {
        var containers = new List<OrchestratorContainerInfo>
        {
            CreateContainerInfo("container1", "run1"),
            CreateContainerInfo("container2", "run2")
        };

        _dockerServiceMock
            .Setup(x => x.ListOrchestratorContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(containers);

        var reconciler = new ContainerOrphanReconciler(_dockerServiceMock.Object, _loggerMock.Object);
        var activeRunIds = new List<string> { "run1", "run2" };

        var result = await reconciler.ReconcileAsync(activeRunIds, CancellationToken.None);

        result.OrphanedCount.Should().Be(0);
        result.RemovedContainers.Should().BeEmpty();
    }

    [Fact]
    public async Task ReconcileAsync_WithOrphanedContainers_ReturnsOrphans()
    {
        var containers = new List<OrchestratorContainerInfo>
        {
            CreateContainerInfo("container1", "run1"),
            CreateContainerInfo("container2", "orphan-run")
        };

        _dockerServiceMock
            .Setup(x => x.ListOrchestratorContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(containers);
        _dockerServiceMock
            .Setup(x => x.RemoveContainerForceAsync("container2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var reconciler = new ContainerOrphanReconciler(_dockerServiceMock.Object, _loggerMock.Object);
        var activeRunIds = new List<string> { "run1" };

        var result = await reconciler.ReconcileAsync(activeRunIds, CancellationToken.None);

        result.OrphanedCount.Should().Be(1);
        result.RemovedContainers.Should().HaveCount(1);
        result.RemovedContainers[0].ContainerId.Should().Be("container2");
        result.RemovedContainers[0].RunId.Should().Be("orphan-run");
    }

    [Fact]
    public async Task ReconcileAsync_WithEmptyActiveRunIds_TreatsAllAsOrphans()
    {
        var containers = new List<OrchestratorContainerInfo>
        {
            CreateContainerInfo("container1", "run1"),
            CreateContainerInfo("container2", "run2")
        };

        _dockerServiceMock
            .Setup(x => x.ListOrchestratorContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(containers);
        _dockerServiceMock
            .Setup(x => x.RemoveContainerForceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var reconciler = new ContainerOrphanReconciler(_dockerServiceMock.Object, _loggerMock.Object);
        var activeRunIds = new List<string>();

        var result = await reconciler.ReconcileAsync(activeRunIds, CancellationToken.None);

        result.OrphanedCount.Should().Be(2);
        result.RemovedContainers.Should().HaveCount(2);
    }

    [Fact]
    public async Task ReconcileAsync_WithFailedRemoval_ReportsCorrectCount()
    {
        var containers = new List<OrchestratorContainerInfo>
        {
            CreateContainerInfo("container1", "orphan1"),
            CreateContainerInfo("container2", "orphan2")
        };

        _dockerServiceMock
            .Setup(x => x.ListOrchestratorContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(containers);
        _dockerServiceMock
            .Setup(x => x.RemoveContainerForceAsync("container1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _dockerServiceMock
            .Setup(x => x.RemoveContainerForceAsync("container2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var reconciler = new ContainerOrphanReconciler(_dockerServiceMock.Object, _loggerMock.Object);
        var activeRunIds = new List<string>();

        var result = await reconciler.ReconcileAsync(activeRunIds, CancellationToken.None);

        result.OrphanedCount.Should().Be(2);
        result.RemovedContainers.Should().HaveCount(1);
        result.RemovedContainers[0].ContainerId.Should().Be("container1");
    }

    [Fact]
    public async Task ReconcileAsync_IsCaseInsensitive()
    {
        var containers = new List<OrchestratorContainerInfo>
        {
            CreateContainerInfo("container1", "RUN1")
        };

        _dockerServiceMock
            .Setup(x => x.ListOrchestratorContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(containers);

        var reconciler = new ContainerOrphanReconciler(_dockerServiceMock.Object, _loggerMock.Object);
        var activeRunIds = new List<string> { "run1" };

        var result = await reconciler.ReconcileAsync(activeRunIds, CancellationToken.None);

        result.OrphanedCount.Should().Be(0);
    }

    [Fact]
    public async Task ReconcileAsync_WithCancellationToken_PropagatesCancellation()
    {
        _dockerServiceMock
            .Setup(x => x.ListOrchestratorContainersAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var reconciler = new ContainerOrphanReconciler(_dockerServiceMock.Object, _loggerMock.Object);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await reconciler.ReconcileAsync([], cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static OrchestratorContainerInfo CreateContainerInfo(string containerId, string runId)
    {
        return new OrchestratorContainerInfo
        {
            ContainerId = containerId,
            RunId = runId,
            TaskId = $"task-{runId}",
            RepoId = $"repo-{runId}",
            ProjectId = $"project-{runId}",
            State = "running",
            Image = "test-image:latest",
            CreatedAt = DateTime.UtcNow
        };
    }
}

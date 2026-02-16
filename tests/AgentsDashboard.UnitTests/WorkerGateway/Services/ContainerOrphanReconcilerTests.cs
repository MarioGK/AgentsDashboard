using AgentsDashboard.WorkerGateway.Models;
using AgentsDashboard.WorkerGateway.Services;
using Microsoft.Extensions.Logging;

namespace AgentsDashboard.UnitTests.WorkerGateway.Services;

[Property("Category", "Docker")]
public class ContainerOrphanReconcilerTests
{
    private readonly Mock<ILogger<ContainerOrphanReconciler>> _loggerMock;

    public ContainerOrphanReconcilerTests()
    {
        _loggerMock = new Mock<ILogger<ContainerOrphanReconciler>>();
    }

    [Test]
    public void Implements_IContainerOrphanReconciler()
    {
        typeof(ContainerOrphanReconciler).Should().Implement<IContainerOrphanReconciler>();
    }

    [Test]
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

    [Test]
    public void OrphanedContainer_PropertiesAreSetCorrectly()
    {
        var container = new OrphanedContainer("abc123", "run-456");

        container.ContainerId.Should().Be("abc123");
        container.RunId.Should().Be("run-456");
    }

    [Test]
    public void OrphanReconciliationResult_WithEmptyContainers_IsValid()
    {
        var result = new OrphanReconciliationResult(0, []);

        result.OrphanedCount.Should().Be(0);
        result.RemovedContainers.Should().BeEmpty();
    }

    [Test]
    public void OrphanedContainer_IsRecord_EqualityWorks()
    {
        var container1 = new OrphanedContainer("abc", "run1");
        var container2 = new OrphanedContainer("abc", "run1");
        var container3 = new OrphanedContainer("xyz", "run2");

        container1.Should().Be(container2);
        container1.Should().NotBe(container3);
    }

    [Test]
    public void OrphanReconciliationResult_IsRecord_EqualityWorks()
    {
        var containers = new List<OrphanedContainer> { new("abc", "run1") };
        var result1 = new OrphanReconciliationResult(1, containers);
        var result2 = new OrphanReconciliationResult(1, containers);
        var result3 = new OrphanReconciliationResult(2, []);

        result1.Should().Be(result2);
        result1.Should().NotBe(result3);
    }

    [Test, Skip("Requires Docker - DockerContainerService is sealed and creates DockerClient in constructor")]
    [Property("Requires", "Docker")]
    public async Task ReconcileAsync_WithNoContainers_ReturnsZeroOrphans()
    {
    }

    [Test, Skip("Requires Docker - DockerContainerService is sealed and creates DockerClient in constructor")]
    [Property("Requires", "Docker")]
    public async Task ReconcileAsync_WithAllActiveRuns_ReturnsZeroOrphans()
    {
    }

    [Test, Skip("Requires Docker - DockerContainerService is sealed and creates DockerClient in constructor")]
    [Property("Requires", "Docker")]
    public async Task ReconcileAsync_WithOrphanedContainers_ReturnsOrphans()
    {
    }

    [Test, Skip("Requires Docker - DockerContainerService is sealed and creates DockerClient in constructor")]
    [Property("Requires", "Docker")]
    public async Task ReconcileAsync_WithEmptyActiveRunIds_TreatsAllAsOrphans()
    {
    }

    [Test, Skip("Requires Docker - DockerContainerService is sealed and creates DockerClient in constructor")]
    [Property("Requires", "Docker")]
    public async Task ReconcileAsync_WithFailedRemoval_ReportsCorrectCount()
    {
    }

    [Test, Skip("Requires Docker - DockerContainerService is sealed and creates DockerClient in constructor")]
    [Property("Requires", "Docker")]
    public async Task ReconcileAsync_IsCaseInsensitive()
    {
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

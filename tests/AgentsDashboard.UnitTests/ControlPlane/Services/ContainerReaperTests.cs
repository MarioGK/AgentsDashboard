using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.ControlPlane.Services;
using MagicOnion;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class ContainerReaperTests
{
    private readonly Mock<IMagicOnionClientFactory> _mockFactory;
    private readonly Mock<IWorkerGatewayService> _mockClient;
    private readonly ContainerReaper _reaper;

    public ContainerReaperTests()
    {
        _mockFactory = new Mock<IMagicOnionClientFactory>();
        _mockClient = new Mock<IWorkerGatewayService>();
        _mockFactory.Setup(f => f.CreateWorkerGatewayService()).Returns(_mockClient.Object);
        _reaper = new ContainerReaper(_mockFactory.Object, Microsoft.Extensions.Logging.Abstractions.NullLogger<ContainerReaper>.Instance);
    }

    [Test]
    public async Task KillContainerAsync_WhenSuccessful_ReturnsKilledResult()
    {
        var response = new KillContainerReply { Success = true, WasRunning = true };
        SetupKillContainerAsync(response);

        var result = await _reaper.KillContainerAsync("run-1", "Test reason", force: false, CancellationToken.None);

        result.Killed.Should().BeTrue();
        result.ContainerId.Should().Be("run-1");
        result.Error.Should().BeEmpty();
    }

    [Test]
    public async Task KillContainerAsync_WhenFailed_ReturnsNotKilledResult()
    {
        var response = new KillContainerReply { Success = false, ErrorMessage = "Container not found" };
        SetupKillContainerAsync(response);

        var result = await _reaper.KillContainerAsync("run-1", "Test reason", force: true, CancellationToken.None);

        result.Killed.Should().BeFalse();
        result.Error.Should().Be("Container not found");
    }

    [Test]
    public async Task KillContainerAsync_WhenException_ReturnsNotKilledResult()
    {
        _mockClient.Setup(c => c.KillContainerAsync(It.IsAny<KillContainerRequest>()))
            .Returns(new UnaryResult<KillContainerReply>(Task.FromException<KillContainerReply>(new Exception("Service unavailable"))));

        var result = await _reaper.KillContainerAsync("run-1", "Test reason", force: false, CancellationToken.None);

        result.Killed.Should().BeFalse();
        result.Error.Should().Contain("Service unavailable");
    }

    [Test]
    public async Task ReapOrphanedContainersAsync_WhenOrphansFound_ReturnsCount()
    {
        var response = new ReconcileOrphanedContainersReply
        {
            Success = true,
            ReconciledCount = 3,
            ContainerIds = ["c1", "c2", "c3"]
        };
        SetupReconcileOrphanedContainersAsync(response);

        var activeRunIds = new List<string> { "run-4", "run-5" };
        var result = await _reaper.ReapOrphanedContainersAsync(activeRunIds, CancellationToken.None);

        result.Should().Be(3);
    }

    [Test]
    public async Task ReapOrphanedContainersAsync_WhenNoOrphans_ReturnsZero()
    {
        var response = new ReconcileOrphanedContainersReply { Success = true, ReconciledCount = 0 };
        SetupReconcileOrphanedContainersAsync(response);

        var activeRunIds = new List<string> { "run-1", "run-2" };
        var result = await _reaper.ReapOrphanedContainersAsync(activeRunIds, CancellationToken.None);

        result.Should().Be(0);
    }

    [Test]
    public async Task ReapOrphanedContainersAsync_WhenException_ReturnsZero()
    {
        _mockClient.Setup(c => c.ReconcileOrphanedContainersAsync(It.IsAny<ReconcileOrphanedContainersRequest>()))
            .Returns(new UnaryResult<ReconcileOrphanedContainersReply>(Task.FromException<ReconcileOrphanedContainersReply>(new Exception("Service unavailable"))));

        var activeRunIds = new List<string> { "run-1" };
        var result = await _reaper.ReapOrphanedContainersAsync(activeRunIds, CancellationToken.None);

        result.Should().Be(0);
    }

    [Test]
    public async Task ReapOrphanedContainersAsync_CallsReconcileOnWorker()
    {
        var response = new ReconcileOrphanedContainersReply { Success = true, ReconciledCount = 0 };
        SetupReconcileOrphanedContainersAsync(response);

        var activeRunIds = new List<string> { "run-1", "run-2", "run-3" };
        await _reaper.ReapOrphanedContainersAsync(activeRunIds, CancellationToken.None);

        _mockClient.Verify(c => c.ReconcileOrphanedContainersAsync(
            It.Is<ReconcileOrphanedContainersRequest>(r => r.WorkerId == "control-plane")), Times.Once);
    }

    private void SetupKillContainerAsync(KillContainerReply response)
    {
        _mockClient.Setup(c => c.KillContainerAsync(It.IsAny<KillContainerRequest>()))
            .Returns(UnaryResult.FromResult(response));
    }

    private void SetupReconcileOrphanedContainersAsync(ReconcileOrphanedContainersReply response)
    {
        _mockClient.Setup(c => c.ReconcileOrphanedContainersAsync(It.IsAny<ReconcileOrphanedContainersRequest>()))
            .Returns(UnaryResult.FromResult(response));
    }
}

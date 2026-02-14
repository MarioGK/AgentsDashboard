using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.ControlPlane.Services;
using FluentAssertions;
using Grpc.Core;
using Moq;
using WorkerGatewayClient = AgentsDashboard.Contracts.Worker.WorkerGateway.WorkerGatewayClient;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class ContainerReaperTests
{
    private readonly Mock<WorkerGatewayClient> _mockClient;
    private readonly ContainerReaper _reaper;

    public ContainerReaperTests()
    {
        _mockClient = new Mock<WorkerGatewayClient>();
        _reaper = new ContainerReaper(_mockClient.Object, Microsoft.Extensions.Logging.Abstractions.NullLogger<ContainerReaper>.Instance);
    }

    [Fact]
    public async Task KillContainerAsync_WhenSuccessful_ReturnsKilledResult()
    {
        var response = new KillContainerReply { Killed = true, ContainerId = "container-123" };
        SetupKillContainerAsync(response);

        var result = await _reaper.KillContainerAsync("run-1", "Test reason", force: false, CancellationToken.None);

        result.Killed.Should().BeTrue();
        result.ContainerId.Should().Be("container-123");
        result.Error.Should().BeEmpty();
    }

    [Fact]
    public async Task KillContainerAsync_WhenFailed_ReturnsNotKilledResult()
    {
        var response = new KillContainerReply { Killed = false, Error = "Container not found" };
        SetupKillContainerAsync(response);

        var result = await _reaper.KillContainerAsync("run-1", "Test reason", force: true, CancellationToken.None);

        result.Killed.Should().BeFalse();
        result.Error.Should().Be("Container not found");
    }

    [Fact]
    public async Task KillContainerAsync_WhenException_ReturnsNotKilledResult()
    {
        _mockClient.Setup(c => c.KillContainerAsync(
                It.IsAny<KillContainerRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => ThrowAsync<KillContainerReply>(new RpcException(new Status(StatusCode.Unavailable, "Service unavailable"))));

        var result = await _reaper.KillContainerAsync("run-1", "Test reason", force: false, CancellationToken.None);

        result.Killed.Should().BeFalse();
        result.Error.Should().Contain("Service unavailable");
    }

    [Fact]
    public async Task ReapOrphanedContainersAsync_WhenOrphansFound_ReturnsCount()
    {
        var response = new ReconcileOrphanedContainersReply
        {
            OrphanedCount = 3
        };
        response.RemovedContainers.Add(new OrphanedContainerInfo { ContainerId = "c1", RunId = "run-1" });
        response.RemovedContainers.Add(new OrphanedContainerInfo { ContainerId = "c2", RunId = "run-2" });
        response.RemovedContainers.Add(new OrphanedContainerInfo { ContainerId = "c3", RunId = "run-3" });
        SetupReconcileOrphanedContainersAsync(response);

        var activeRunIds = new List<string> { "run-4", "run-5" };
        var result = await _reaper.ReapOrphanedContainersAsync(activeRunIds, CancellationToken.None);

        result.Should().Be(3);
    }

    [Fact]
    public async Task ReapOrphanedContainersAsync_WhenNoOrphans_ReturnsZero()
    {
        var response = new ReconcileOrphanedContainersReply { OrphanedCount = 0 };
        SetupReconcileOrphanedContainersAsync(response);

        var activeRunIds = new List<string> { "run-1", "run-2" };
        var result = await _reaper.ReapOrphanedContainersAsync(activeRunIds, CancellationToken.None);

        result.Should().Be(0);
    }

    [Fact]
    public async Task ReapOrphanedContainersAsync_WhenException_ReturnsZero()
    {
        _mockClient.Setup(c => c.ReconcileOrphanedContainersAsync(
                It.IsAny<ReconcileOrphanedContainersRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => ThrowAsync<ReconcileOrphanedContainersReply>(new RpcException(new Status(StatusCode.Unavailable, "Service unavailable"))));

        var activeRunIds = new List<string> { "run-1" };
        var result = await _reaper.ReapOrphanedContainersAsync(activeRunIds, CancellationToken.None);

        result.Should().Be(0);
    }

    [Fact]
    public async Task ReapOrphanedContainersAsync_PassesActiveRunIdsToRequest()
    {
        ReconcileOrphanedContainersRequest? capturedRequest = null;
        _mockClient.Setup(c => c.ReconcileOrphanedContainersAsync(
                It.IsAny<ReconcileOrphanedContainersRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Callback<ReconcileOrphanedContainersRequest, Metadata, DateTime?, CancellationToken>(
                (req, _, _, _) => capturedRequest = req)
            .Returns(() => CreateAsyncUnaryCall(new ReconcileOrphanedContainersReply { OrphanedCount = 0 }));

        var activeRunIds = new List<string> { "run-1", "run-2", "run-3" };
        await _reaper.ReapOrphanedContainersAsync(activeRunIds, CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.ActiveRunIds.Should().Contain("run-1");
        capturedRequest.ActiveRunIds.Should().Contain("run-2");
        capturedRequest.ActiveRunIds.Should().Contain("run-3");
    }

    private void SetupKillContainerAsync(KillContainerReply response)
    {
        _mockClient.Setup(c => c.KillContainerAsync(
                It.IsAny<KillContainerRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => CreateAsyncUnaryCall(response));
    }

    private void SetupReconcileOrphanedContainersAsync(ReconcileOrphanedContainersReply response)
    {
        _mockClient.Setup(c => c.ReconcileOrphanedContainersAsync(
                It.IsAny<ReconcileOrphanedContainersRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => CreateAsyncUnaryCall(response));
    }

    private static AsyncUnaryCall<T> CreateAsyncUnaryCall<T>(T response)
    {
        return new AsyncUnaryCall<T>(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => new Metadata());
    }

    private static AsyncUnaryCall<T> ThrowAsync<T>(Exception ex)
    {
        return new AsyncUnaryCall<T>(
            Task.FromException<T>(ex),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => new Metadata());
    }
}

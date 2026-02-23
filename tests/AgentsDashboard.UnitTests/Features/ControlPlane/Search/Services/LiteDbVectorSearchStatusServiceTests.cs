using AgentsDashboard.ControlPlane.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public sealed class LiteDbVectorSearchStatusServiceTests
{
    [Test]
    public async Task StartAsync_QueuesLiteDbVectorBootstrap_AndReportsAvailable()
    {
        Func<CancellationToken, IProgress<BackgroundWorkSnapshot>, Task>? capturedWork = null;
        var coordinator = new Mock<IBackgroundWorkCoordinator>(MockBehavior.Strict);
        coordinator
            .Setup(x => x.Enqueue(
                It.IsAny<BackgroundWorkKind>(),
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, IProgress<BackgroundWorkSnapshot>, Task>>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()))
            .Callback((
                BackgroundWorkKind _,
                string _,
                Func<CancellationToken, IProgress<BackgroundWorkSnapshot>, Task> work,
                bool _,
                bool _) => capturedWork = work)
            .Returns("work-1");

        var service = new LiteDbVectorSearchStatusService(coordinator.Object, NullLogger<LiteDbVectorSearchStatusService>.Instance);

        await service.StartAsync(CancellationToken.None);

        capturedWork.Should().NotBeNull();
        var snapshots = new List<BackgroundWorkSnapshot>();
        await capturedWork!(CancellationToken.None, new Progress<BackgroundWorkSnapshot>(snapshot => snapshots.Add(snapshot)));

        service.IsAvailable.Should().BeTrue();
        service.Status.IsAvailable.Should().BeTrue();
        service.Status.Detail.Should().Be("LiteDB vector mode active");
        snapshots.Should().ContainSingle(x =>
            x.Kind == BackgroundWorkKind.LiteDbVectorBootstrap &&
            x.State == BackgroundWorkState.Succeeded);

        coordinator.VerifyAll();
    }
}

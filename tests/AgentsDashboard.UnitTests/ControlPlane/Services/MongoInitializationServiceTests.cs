using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class MongoInitializationServiceTests
{
    private readonly Mock<IOrchestratorStore> _storeMock;
    private readonly Mock<ILogger<MongoInitializationService>> _loggerMock;

    public MongoInitializationServiceTests()
    {
        _storeMock = new Mock<IOrchestratorStore>(MockBehavior.Strict);
        _loggerMock = new Mock<ILogger<MongoInitializationService>>();
    }

    private MongoInitializationService CreateService()
    {
        return new MongoInitializationService(_storeMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task StartAsync_CallsStoreInitializeAsync()
    {
        _storeMock.Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();
        var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);

        _storeMock.Verify(s => s.InitializeAsync(cts.Token), Times.Once);
    }

    [Fact]
    public async Task StartAsync_LogsInformationOnSuccess()
    {
        _storeMock.Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        await service.StartAsync(CancellationToken.None);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Mongo collections and indexes initialized")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task StartAsync_PropagatesExceptionFromStore()
    {
        var expectedException = new InvalidOperationException("Mongo connection failed");
        _storeMock.Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        var service = CreateService();

        var act = () => service.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Mongo connection failed");
    }

    [Fact]
    public void StopAsync_ReturnsCompletedTask()
    {
        var service = CreateService();

        var result = service.StopAsync(CancellationToken.None);

        result.IsCompleted.Should().BeTrue();
        result.Status.Should().Be(TaskStatus.RanToCompletion);
    }

    [Fact]
    public async Task StartAsync_PassesCancellationTokenToStore()
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        _storeMock.Setup(s => s.InitializeAsync(token))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        await service.StartAsync(token);

        _storeMock.Verify(s => s.InitializeAsync(token), Times.Once);
    }

    [Fact]
    public async Task StartAsync_WithCancelledToken_PassesToStore()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        _storeMock.Setup(s => s.InitializeAsync(cts.Token))
            .ThrowsAsync(new OperationCanceledException());

        var service = CreateService();

        var act = () => service.StartAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

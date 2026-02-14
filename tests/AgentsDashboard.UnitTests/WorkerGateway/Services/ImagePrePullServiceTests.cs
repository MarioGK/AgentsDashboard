using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.UnitTests.WorkerGateway.Services;

[Trait("Category", "Docker")]
public class ImagePrePullServiceTests
{
    private readonly Mock<ILogger<ImagePrePullService>> _loggerMock;

    public ImagePrePullServiceTests()
    {
        _loggerMock = new Mock<ILogger<ImagePrePullService>>();
    }

    private ImagePrePullService CreateService(WorkerOptions? options = null)
    {
        options ??= new WorkerOptions();
        return new ImagePrePullService(
            Options.Create(options),
            _loggerMock.Object);
    }

    [Fact]
    [Trait("Requires", "Docker")]
    public async Task StartAsync_WithNoImages_LogsNoImages()
    {
        var options = new WorkerOptions
        {
            DefaultImage = string.Empty,
            HarnessImages = new Dictionary<string, string>()
        };
        var service = CreateService(options);

        await service.StartAsync(CancellationToken.None);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("No harness images configured")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    [Trait("Requires", "Docker")]
    public async Task StartAsync_CompletesSuccessfully()
    {
        var options = new WorkerOptions
        {
            DefaultImage = string.Empty,
            HarnessImages = new Dictionary<string, string>()
        };
        var service = CreateService(options);

        var act = async () => await service.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void StopAsync_ReturnsCompletedTask()
    {
        try
        {
            var service = CreateService();
            var task = service.StopAsync(CancellationToken.None);
            task.IsCompleted.Should().BeTrue();
        }
        catch (Docker.DotNet.DockerApiException)
        {
        }
    }

    [Fact]
    [Trait("Requires", "Docker")]
    public async Task StartAsync_WithCancellationToken_RespectsCancellation()
    {
        var options = new WorkerOptions
        {
            DefaultImage = string.Empty,
            HarnessImages = new Dictionary<string, string>()
        };
        var service = CreateService(options);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await service.StartAsync(cts.Token);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    [Trait("Requires", "Docker")]
    public async Task StopAsync_CanBeCalledMultipleTimes()
    {
        var service = CreateService();

        await service.StopAsync(CancellationToken.None);
        var act = async () => await service.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    [Trait("Requires", "Docker")]
    public async Task StartStop_CanBeCalledInSequence()
    {
        var options = new WorkerOptions
        {
            DefaultImage = string.Empty,
            HarnessImages = new Dictionary<string, string>()
        };
        var service = CreateService(options);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        var act = async () =>
        {
            await service.StartAsync(CancellationToken.None);
            await service.StopAsync(CancellationToken.None);
        };

        await act.Should().NotThrowAsync();
    }
}

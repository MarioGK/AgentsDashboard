using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.UnitTests.WorkerGateway.Services;

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
    public async Task StartAsync_WithEmptyHarnessImages_UsesDefaultImageOnly()
    {
        var options = new WorkerOptions
        {
            DefaultImage = "test-image:latest",
            HarnessImages = new Dictionary<string, string>()
        };
        var service = CreateService(options);

        await service.StartAsync(CancellationToken.None);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("pre-pull of 1 harness images")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task StartAsync_WithMultipleImages_LogsImageCount()
    {
        var options = new WorkerOptions
        {
            DefaultImage = "default:latest",
            HarnessImages = new Dictionary<string, string>
            {
                ["codex"] = "codex:latest",
                ["opencode"] = "opencode:latest"
            }
        };
        var service = CreateService(options);

        await service.StartAsync(CancellationToken.None);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("pre-pull of") && v.ToString()!.Contains("harness images")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task StartAsync_WithDuplicateImages_Deduplicates()
    {
        var options = new WorkerOptions
        {
            DefaultImage = "same-image:latest",
            HarnessImages = new Dictionary<string, string>
            {
                ["codex"] = "same-image:latest",
                ["opencode"] = "same-image:latest"
            }
        };
        var service = CreateService(options);

        await service.StartAsync(CancellationToken.None);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("pre-pull of 1 harness images")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
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
    public async Task StopAsync_ReturnsCompletedTask()
    {
        var service = CreateService();

        var task = service.StopAsync(CancellationToken.None);

        task.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_WithCancellationToken_RespectsCancellation()
    {
        var options = new WorkerOptions
        {
            DefaultImage = "test:latest",
            HarnessImages = new Dictionary<string, string>()
        };
        var service = CreateService(options);
        using var cts = new CancellationTokenSource();

        cts.Cancel();

        var act = async () => await service.StartAsync(cts.Token);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_WithNullDefaultImage_HandlesGracefully()
    {
        var options = new WorkerOptions
        {
            DefaultImage = null!,
            HarnessImages = new Dictionary<string, string>()
        };
        var service = CreateService(options);

        var act = async () => await service.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_WithWhitespaceDefaultImage_IgnoresWhitespace()
    {
        var options = new WorkerOptions
        {
            DefaultImage = "   ",
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
    public async Task StartAsync_WithNullHarnessImageValue_HandlesGracefully()
    {
        var options = new WorkerOptions
        {
            DefaultImage = string.Empty,
            HarnessImages = new Dictionary<string, string>
            {
                ["codex"] = null!
            }
        };
        var service = CreateService(options);

        var act = async () => await service.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Service_ImplementsIHostedService()
    {
        var service = CreateService();

        service.Should().BeAssignableTo<IHostedService>();
    }

    [Fact]
    public async Task StartAsync_WithAllHarnessImages_LogsCorrectCount()
    {
        var options = new WorkerOptions
        {
            DefaultImage = "default:latest",
            HarnessImages = new Dictionary<string, string>
            {
                ["codex"] = "codex:latest",
                ["opencode"] = "opencode:latest",
                ["claude code"] = "claudecode:latest",
                ["zai"] = "zai:latest"
            }
        };
        var service = CreateService(options);

        await service.StartAsync(CancellationToken.None);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("pre-pull of 5 harness images")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task StartAsync_CompletesWithoutDocker()
    {
        var options = new WorkerOptions
        {
            DefaultImage = "nonexistent:latest",
            HarnessImages = new Dictionary<string, string>()
        };
        var service = CreateService(options);

        var act = async () => await service.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopAsync_CanBeCalledMultipleTimes()
    {
        var service = CreateService();

        await service.StopAsync(CancellationToken.None);
        var act = async () => await service.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
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

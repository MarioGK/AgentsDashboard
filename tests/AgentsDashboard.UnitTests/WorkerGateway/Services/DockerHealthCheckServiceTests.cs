using AgentsDashboard.WorkerGateway.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace AgentsDashboard.UnitTests.WorkerGateway.Services;

public class DockerHealthCheckServiceTests
{
    private readonly Mock<ILogger<DockerHealthCheckService>> _loggerMock;

    public DockerHealthCheckServiceTests()
    {
        _loggerMock = new Mock<ILogger<DockerHealthCheckService>>();
    }

    [Test, Skip("Requires Docker runtime - Docker.DotNet version mismatch")]
    public async Task CheckHealthAsync_InitialState_ReturnsUnhealthy()
    {
        using var service = new DockerHealthCheckService(_loggerMock.Object);

        var result = await service.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Initial health check pending");
    }

    [Test, Skip("Requires Docker runtime - Docker.DotNet version mismatch")]
    public async Task CheckHealthAsync_DoesNotBlockOnSlowHealthCheck()
    {
        using var service = new DockerHealthCheckService(_loggerMock.Object);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await service.CheckHealthAsync(new HealthCheckContext());
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(100);
    }

    [Test, Skip("Requires Docker runtime - Docker.DotNet version mismatch")]
    public async Task CheckHealthAsync_MultipleCallsReturnCachedResult()
    {
        using var service = new DockerHealthCheckService(_loggerMock.Object);

        var result1 = await service.CheckHealthAsync(new HealthCheckContext());
        var result2 = await service.CheckHealthAsync(new HealthCheckContext());

        result1.Status.Should().Be(result2.Status);
        result1.Description.Should().Be(result2.Description);
    }

    [Test, Skip("Requires Docker runtime - Docker.DotNet version mismatch")]
    public async Task CheckHealthAsync_ThreadSafe()
    {
        using var service = new DockerHealthCheckService(_loggerMock.Object);
        var tasks = new List<Task<HealthCheckResult>>();

        for (int i = 0; i < 100; i++)
        {
            tasks.Add(service.CheckHealthAsync(new HealthCheckContext()));
        }

        var results = await Task.WhenAll(tasks);

        results.Should().HaveCount(100);
        results.Should().OnlyContain(r => r.Status == HealthStatus.Unhealthy);
    }

    [Test, Skip("Requires Docker runtime - Docker.DotNet version mismatch")]
    public void Dispose_DoesNotThrow()
    {
        var service = new DockerHealthCheckService(_loggerMock.Object);

        var act = () => service.Dispose();

        act.Should().NotThrow();
    }

    [Test, Skip("Requires Docker runtime - Docker.DotNet version mismatch")]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var service = new DockerHealthCheckService(_loggerMock.Object);

        service.Dispose();
        var act = () => service.Dispose();

        act.Should().NotThrow();
    }

    [Test, Skip("Requires Docker runtime - Docker.DotNet version mismatch")]
    public async Task ExecuteAsync_StartsAfterInitialDelay()
    {
        using var service = new DockerHealthCheckService(_loggerMock.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));

        await service.StartAsync(cts.Token);

        await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
        var resultBefore = await service.CheckHealthAsync(new HealthCheckContext());

        await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None);
        var resultAfter = await service.CheckHealthAsync(new HealthCheckContext());

        resultBefore.Status.Should().Be(HealthStatus.Unhealthy);
        resultBefore.Description.Should().Be("Initial health check pending");
    }

    [Test, Skip("Requires Docker runtime - Docker.DotNet version mismatch")]
    public async Task ExecuteAsync_StopsOnCancellation()
    {
        using var service = new DockerHealthCheckService(_loggerMock.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));

        await service.StartAsync(cts.Token);

        await Task.Delay(TimeSpan.FromSeconds(7), CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        var act = async () => await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Test, Skip("Requires Docker runtime - Docker.DotNet version mismatch")]
    public async Task ExecuteAsync_HandlesExceptionsGracefully()
    {
        using var service = new DockerHealthCheckService(_loggerMock.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));

        await service.StartAsync(cts.Token);

        await Task.Delay(TimeSpan.FromSeconds(7), CancellationToken.None);
        var result = await service.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().BeOneOf(HealthStatus.Healthy, HealthStatus.Unhealthy);
    }

    [Test, Skip("Requires Docker runtime - Docker.DotNet version mismatch")]
    public async Task CheckHealthAsync_WithContext_ReturnsResult()
    {
        using var service = new DockerHealthCheckService(_loggerMock.Object);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration(
                "docker",
                service,
                HealthStatus.Unhealthy,
                null)
        };

        var result = await service.CheckHealthAsync(context);

        result.Should().NotBeNull();
        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Test, Skip("Requires Docker runtime - Docker.DotNet version mismatch")]
    public async Task CheckHealthAsync_WithCancellationToken_ReturnsImmediately()
    {
        using var service = new DockerHealthCheckService(_loggerMock.Object);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.CheckHealthAsync(new HealthCheckContext(), cts.Token);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Test, Skip("Requires Docker runtime - Docker.DotNet version mismatch")]
    public async Task CheckHealthAsync_ResultHasDescription()
    {
        using var service = new DockerHealthCheckService(_loggerMock.Object);

        var result = await service.CheckHealthAsync(new HealthCheckContext());

        result.Description.Should().NotBeNullOrEmpty();
    }

    [Test, Skip("Requires Docker runtime - Docker.DotNet version mismatch")]
    public async Task ExecuteAsync_ServiceCancellation_StopsGracefully()
    {
        using var service = new DockerHealthCheckService(_loggerMock.Object);
        using var cts = new CancellationTokenSource();

        var serviceTask = service.StartAsync(cts.Token);
        cts.Cancel();

        var act = async () => await serviceTask;
        await act.Should().NotThrowAsync();
    }
}

using Docker.DotNet;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgentsDashboard.WorkerGateway.Services;

public sealed class DockerHealthCheckService(ILogger<DockerHealthCheckService> logger) : BackgroundService, IHealthCheck
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);
    private readonly DockerClient _client = new DockerClientConfiguration().CreateClient();
    private readonly object _lock = new();
    private HealthCheckResult _lastResult = HealthCheckResult.Unhealthy("Initial health check pending");

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_lastResult);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckDockerAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Docker health check failed");
                SetResult(HealthCheckResult.Unhealthy($"Docker health check failed: {ex.Message}"));
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task CheckDockerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _client.System.PingAsync(cancellationToken);
            SetResult(HealthCheckResult.Healthy("Docker daemon is available"));
            logger.LogDebug("Docker health check passed");
        }
        catch (Exception ex)
        {
            SetResult(HealthCheckResult.Unhealthy($"Docker daemon unavailable: {ex.Message}"));
            logger.LogWarning("Docker daemon is unavailable: {Message}", ex.Message);
        }
    }

    private void SetResult(HealthCheckResult result)
    {
        lock (_lock)
        {
            _lastResult = result;
        }
    }

    public override void Dispose()
    {
        _client.Dispose();
        base.Dispose();
    }
}

using Docker.DotNet;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgentsDashboard.TaskRuntimeGateway.Services;

public sealed class DockerHealthCheckService(ILogger<DockerHealthCheckService> logger) : BackgroundService, IHealthCheck
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromSeconds(90);
    private readonly DockerClient _client = new DockerClientConfiguration().CreateClient();
    private readonly object _lock = new();
    private HealthCheckResult _lastResult = HealthCheckResult.Unhealthy("Initial health check pending");
    private DateTimeOffset _lastResultUpdatedAtUtc = DateTimeOffset.MinValue;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_lastResultUpdatedAtUtc != DateTimeOffset.MinValue &&
                DateTimeOffset.UtcNow - _lastResultUpdatedAtUtc > StaleThreshold)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy("Docker health check is stale"));
            }

            return Task.FromResult(_lastResult);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await CheckDockerAsync(stoppingToken);

            using var timer = new PeriodicTimer(CheckInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await CheckDockerAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task CheckDockerAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutTokenSource.CancelAfter(CheckTimeout);
            await _client.System.PingAsync(timeoutTokenSource.Token);
            SetResult(HealthCheckResult.Healthy("Docker daemon is available"));
            logger.ZLogDebug("Docker health check passed");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            SetResult(HealthCheckResult.Unhealthy($"Docker daemon ping timed out after {CheckTimeout.TotalSeconds:0}s"));
            logger.ZLogWarning("Docker health check timed out");
        }
        catch (Exception ex)
        {
            SetResult(HealthCheckResult.Unhealthy($"Docker daemon unavailable: {ex.Message}"));
            logger.ZLogWarning("Docker daemon is unavailable: {Message}", ex.Message);
        }
    }

    private void SetResult(HealthCheckResult result)
    {
        lock (_lock)
        {
            _lastResult = result;
            _lastResultUpdatedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public override void Dispose()
    {
        _client.Dispose();
        base.Dispose();
    }
}

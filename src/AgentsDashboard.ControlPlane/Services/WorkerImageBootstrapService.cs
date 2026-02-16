namespace AgentsDashboard.ControlPlane.Services;

public sealed class WorkerImageBootstrapService(
    IWorkerLifecycleManager lifecycleManager,
    ILogger<WorkerImageBootstrapService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await lifecycleManager.EnsureWorkerImageAvailableAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Worker image bootstrap failed");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

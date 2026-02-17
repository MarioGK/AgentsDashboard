using AgentsDashboard.ControlPlane.Data;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class WorkerIdleShutdownService(
    IWorkerLifecycleManager lifecycleManager,
    IOrchestratorStore store,
    ILogger<WorkerIdleShutdownService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await lifecycleManager.EnsureMinimumWorkersAsync(stoppingToken);
                var active = await store.CountActiveRunsAsync(stoppingToken);
                if (active == 0)
                    await lifecycleManager.ScaleDownIdleWorkersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.ZLogDebug(ex, "Worker idle shutdown tick failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}

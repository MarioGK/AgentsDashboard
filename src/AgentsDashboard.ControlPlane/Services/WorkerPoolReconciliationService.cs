namespace AgentsDashboard.ControlPlane.Services;

public sealed class WorkerPoolReconciliationService(
    IWorkerLifecycleManager lifecycleManager,
    ILogger<WorkerPoolReconciliationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await lifecycleManager.EnsureMinimumWorkersAsync(stoppingToken);
                await lifecycleManager.RunReconciliationAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Worker pool reconciliation tick failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}

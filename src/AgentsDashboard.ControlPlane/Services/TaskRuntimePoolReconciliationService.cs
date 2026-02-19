namespace AgentsDashboard.ControlPlane.Services;

public sealed class TaskRuntimePoolReconciliationService(
    ITaskRuntimeLifecycleManager lifecycleManager,
    ILogger<TaskRuntimePoolReconciliationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await lifecycleManager.EnsureMinimumTaskRuntimesAsync(stoppingToken);
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

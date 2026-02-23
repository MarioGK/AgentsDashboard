namespace AgentsDashboard.ControlPlane.Features.RuntimeOrchestration.Services;

public sealed class TaskRuntimeIdleShutdownService(
    ITaskRuntimeLifecycleManager lifecycleManager,
    ILogger<TaskRuntimeIdleShutdownService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await lifecycleManager.ScaleDownIdleTaskRuntimesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Worker idle shutdown tick failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}

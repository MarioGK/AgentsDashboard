namespace AgentsDashboard.TaskRuntime.Services;

public sealed class TaskRuntimeEventBroadcastService(
    TaskRuntimeEventBus eventBus,
    TaskRuntimeEventDispatcher dispatcher,
    ILogger<TaskRuntimeEventBroadcastService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var eventMessage in eventBus.ReadAllAsync(stoppingToken))
        {
            try
            {
                await dispatcher.BroadcastJobEventAsync(eventMessage);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to broadcast worker event for run {RunId}", eventMessage.RunId);
            }
        }
    }
}

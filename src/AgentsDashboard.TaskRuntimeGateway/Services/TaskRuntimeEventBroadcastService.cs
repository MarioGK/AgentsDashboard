using AgentsDashboard.TaskRuntimeGateway.MagicOnion;

namespace AgentsDashboard.TaskRuntimeGateway.Services;

public sealed class TaskRuntimeEventBroadcastService(
    TaskRuntimeEventBus eventBus,
    ILogger<TaskRuntimeEventBroadcastService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var eventMessage in eventBus.ReadAllAsync(stoppingToken))
        {
            try
            {
                await TaskRuntimeEventHub.BroadcastJobEventAsync(eventMessage);
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, "Failed to broadcast worker event for run {RunId}", eventMessage.RunId);
            }
        }
    }
}

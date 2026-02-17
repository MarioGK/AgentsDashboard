using AgentsDashboard.WorkerGateway.MagicOnion;

namespace AgentsDashboard.WorkerGateway.Services;

public sealed class WorkerEventBroadcastService(
    WorkerEventBus eventBus,
    ILogger<WorkerEventBroadcastService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var eventMessage in eventBus.ReadAllAsync(stoppingToken))
        {
            try
            {
                await WorkerEventHub.BroadcastJobEventAsync(eventMessage);
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, "Failed to broadcast worker event for run {RunId}", eventMessage.RunId);
            }
        }
    }
}

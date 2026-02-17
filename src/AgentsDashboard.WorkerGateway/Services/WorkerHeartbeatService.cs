using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.MagicOnion;

namespace AgentsDashboard.WorkerGateway.Services;

public sealed class WorkerHeartbeatService(
    WorkerOptions options,
    IWorkerQueue queue,
    ILogger<WorkerHeartbeatService> logger) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(InitialDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishHeartbeatAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.ZLogWarning(ex, "Failed to publish worker heartbeat");
            }

            await Task.Delay(HeartbeatInterval, stoppingToken);
        }
    }

    private async Task PublishHeartbeatAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var status = new WorkerStatusMessage
        {
            WorkerId = options.WorkerId,
            Status = "healthy",
            ActiveSlots = queue.ActiveSlots,
            MaxSlots = queue.MaxSlots,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Message = "Worker heartbeat"
        };

        await WorkerEventHub.BroadcastWorkerStatusAsync(status);
        logger.ZLogDebug("Heartbeat published successfully: Worker={WorkerId}, Active={Active}/{Max}",
            options.WorkerId, queue.ActiveSlots, queue.MaxSlots);
    }
}

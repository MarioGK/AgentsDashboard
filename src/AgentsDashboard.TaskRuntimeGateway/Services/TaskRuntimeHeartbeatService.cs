using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.TaskRuntimeGateway.Configuration;
using AgentsDashboard.TaskRuntimeGateway.MagicOnion;

namespace AgentsDashboard.TaskRuntimeGateway.Services;

public sealed class TaskRuntimeHeartbeatService(
    TaskRuntimeOptions options,
    ITaskRuntimeQueue queue,
    ILogger<TaskRuntimeHeartbeatService> logger) : BackgroundService
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

        var status = new TaskRuntimeStatusMessage
        {
            TaskRuntimeId = options.TaskRuntimeId,
            Status = "healthy",
            ActiveSlots = queue.ActiveSlots,
            MaxSlots = queue.MaxSlots,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Message = "Worker heartbeat"
        };

        await TaskRuntimeEventHub.BroadcastTaskRuntimeStatusAsync(status);
        logger.ZLogDebug("Heartbeat published successfully: Worker={TaskRuntimeId}, Active={Active}/{Max}",
            options.TaskRuntimeId, queue.ActiveSlots, queue.MaxSlots);
    }
}

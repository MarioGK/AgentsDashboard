using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Features.RuntimeOrchestration.Services;

public sealed class RunQueueDrainService(
    IRunStore runStore,
    RunDispatcher runDispatcher,
    IOptions<OrchestratorOptions> options,
    ILogger<RunQueueDrainService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(2, options.Value.SchedulerIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var queuedTaskIds = await runStore.ListTaskIdsWithQueuedRunsAsync(stoppingToken);
                var dispatched = 0;
                foreach (var taskId in queuedTaskIds)
                {
                    if (string.IsNullOrWhiteSpace(taskId))
                    {
                        continue;
                    }

                    var accepted = await runDispatcher.DispatchNextQueuedRunForTaskAsync(taskId, stoppingToken);
                    if (accepted)
                    {
                        dispatched++;
                    }
                }

                if (dispatched > 0)
                {
                    logger.ZLogInformation($"Run queue drain dispatched {dispatched} queued run(s)");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.ZLogWarning(ex, $"Run queue drain cycle failed");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}

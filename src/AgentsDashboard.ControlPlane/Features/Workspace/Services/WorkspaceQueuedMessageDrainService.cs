using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Features.Workspace.Services;

public sealed class WorkspaceQueuedMessageDrainService(
    IWorkspaceService workspaceService,
    IOptions<OrchestratorOptions> options,
    ILogger<WorkspaceQueuedMessageDrainService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(3, options.Value.SchedulerIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var drainedCount = await workspaceService.DrainQueuedMessagesForAllTasksAsync(stoppingToken);
                if (drainedCount > 0)
                {
                    logger.ZLogInformation($"Workspace queued-message drain processed {drainedCount} task queue(s)");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.ZLogWarning(ex, $"Workspace queued-message drain cycle failed");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}

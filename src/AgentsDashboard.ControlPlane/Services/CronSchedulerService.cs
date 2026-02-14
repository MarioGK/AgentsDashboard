using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class CronSchedulerService(
    OrchestratorStore store,
    RunDispatcher dispatcher,
    IOptions<OrchestratorOptions> options,
    ILogger<CronSchedulerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(2, options.Value.SchedulerIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduler tick failed");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var opts = options.Value;

        // Check global concurrency before fetching due tasks
        var globalActive = await store.CountActiveRunsAsync(cancellationToken);
        if (globalActive >= opts.MaxGlobalConcurrentRuns)
        {
            logger.LogDebug("Global concurrency limit reached ({Active}/{Max}), skipping scheduler tick", globalActive, opts.MaxGlobalConcurrentRuns);
            return;
        }

        var remaining = (int)(opts.MaxGlobalConcurrentRuns - globalActive);
        var dueTasks = await store.ListDueTasksAsync(now, remaining, cancellationToken);

        foreach (var task in dueTasks)
        {
            var repo = await store.GetRepositoryAsync(task.RepositoryId, cancellationToken);
            if (repo is null)
                continue;

            var project = await store.GetProjectAsync(repo.ProjectId, cancellationToken);
            if (project is null)
                continue;

            var run = await store.CreateRunAsync(task, project.Id, cancellationToken);
            await dispatcher.DispatchAsync(project, repo, task, run, cancellationToken);

            if (task.Kind == TaskKind.OneShot)
            {
                await store.MarkOneShotTaskConsumedAsync(task.Id, cancellationToken);
                continue;
            }

            var nextRun = OrchestratorStore.ComputeNextRun(task, now.AddSeconds(1));
            await store.UpdateTaskNextRunAsync(task.Id, nextRun, cancellationToken);
        }
    }
}

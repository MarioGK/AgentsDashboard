using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class CronSchedulerService(
    IOrchestratorStore store,
    RunDispatcher dispatcher,
    IOptions<OrchestratorOptions> options,
    ILogger<CronSchedulerService> logger,
    TimeProvider? timeProvider = null) : BackgroundService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(2, options.Value.SchedulerIntervalSeconds));
        var nextTick = _timeProvider.GetUtcNow().UtcDateTime;

        while (!stoppingToken.IsCancellationRequested)
        {
            nextTick = nextTick.Add(interval);

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

            var delay = nextTick - _timeProvider.GetUtcNow().UtcDateTime;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken);
            }
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
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

            var run = await store.CreateRunAsync(task, cancellationToken);
            await dispatcher.DispatchAsync(repo, task, run, cancellationToken);

            if (task.Kind == TaskKind.OneShot)
            {
                await store.MarkOneShotTaskConsumedAsync(task.Id, cancellationToken);
                continue;
            }

            var nextRun = OrchestratorStore.ComputeNextRun(task, now.AddSeconds(1));
            await store.UpdateTaskNextRunAsync(task.Id, nextRun, cancellationToken);
        }

        await DispatchQueuedTaskHeadsAsync(opts, cancellationToken);
    }

    private async Task DispatchQueuedTaskHeadsAsync(OrchestratorOptions opts, CancellationToken cancellationToken)
    {
        var queuedRuns = await store.ListRunsByStateAsync(RunState.Queued, cancellationToken);
        if (queuedRuns.Count == 0)
        {
            return;
        }

        var queuedTaskIds = queuedRuns
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .Select(x => x.TaskId)
            .Where(taskId => !string.IsNullOrWhiteSpace(taskId))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var taskId in queuedTaskIds)
        {
            var globalActive = await store.CountActiveRunsAsync(cancellationToken);
            if (globalActive >= opts.MaxGlobalConcurrentRuns)
            {
                break;
            }

            await dispatcher.DispatchNextQueuedRunForTaskAsync(taskId, cancellationToken);
        }
    }
}

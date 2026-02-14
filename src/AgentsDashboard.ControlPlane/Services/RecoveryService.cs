using System.Diagnostics;
using System.Diagnostics.Metrics;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Services;

public sealed record DeadRunDetectionResult
{
    public int StaleRunsTerminated { get; set; }
    public int ZombieRunsTerminated { get; set; }
    public int OverdueRunsTerminated { get; set; }
}

public sealed class RecoveryService(
    OrchestratorStore store,
    IRunEventPublisher publisher,
    IContainerReaper containerReaper,
    IOptions<OrchestratorOptions> options,
    ILogger<RecoveryService> logger) : IHostedService, IDisposable
{
    private static readonly Meter s_meter = new("AgentsDashboard.ControlPlane.Recovery");
    private static readonly Counter<int> s_orphanedContainersDetected = s_meter.CreateCounter<int>("orphaned_containers_detected", "containers");
    private static readonly Counter<int> s_orphanedContainersRemoved = s_meter.CreateCounter<int>("orphaned_containers_removed", "containers");

    private readonly DeadRunDetectionConfig _config = options.Value.DeadRunDetection;
    private readonly StageTimeoutConfig _stageTimeoutConfig = options.Value.StageTimeout;
    private Timer? _monitoringTimer;
    private readonly object _lock = new();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Recovery service starting — checking for orphaned runs, workflows, and containers");

        await RecoverOrphanedRunsAsync(cancellationToken);
        await RecoverOrphanedWorkflowExecutionsAsync(cancellationToken);
        await LogPendingApprovalRunsAsync(cancellationToken);
        await LogQueuedRunsAsync(cancellationToken);
        await ReconcileOrphanedContainersAsync(cancellationToken);

        if (_config.EnableAutoTermination)
        {
            StartDeadRunMonitoring();
        }
    }

    private async Task ReconcileOrphanedContainersAsync(CancellationToken cancellationToken)
    {
        try
        {
            var allRunIds = await store.ListAllRunIdsAsync(cancellationToken);

            var orphanedCount = await containerReaper.ReapOrphanedContainersAsync(allRunIds, cancellationToken);

            if (orphanedCount > 0)
            {
                s_orphanedContainersDetected.Add(orphanedCount);
                s_orphanedContainersRemoved.Add(orphanedCount);

                logger.LogWarning(
                    "Container reconciliation complete: {Count} orphaned containers removed",
                    orphanedCount);
            }
            else
            {
                logger.LogInformation("No orphaned containers found during reconciliation");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reconcile orphaned containers");
        }
    }

    private void StartDeadRunMonitoring()
    {
        lock (_lock)
        {
            if (_monitoringTimer is not null)
                return;

            var interval = TimeSpan.FromSeconds(_config.CheckIntervalSeconds);
            _monitoringTimer = new Timer(
                async _ => await MonitorForDeadRunsAsync(),
                null,
                interval,
                interval);

            logger.LogInformation("Dead-run monitoring started with {Interval}s interval", _config.CheckIntervalSeconds);
        }
    }

    internal async Task<DeadRunDetectionResult> MonitorForDeadRunsAsync()
    {
        var result = new DeadRunDetectionResult();
        try
        {
            result.StaleRunsTerminated = await DetectAndTerminateStaleRunsAsync();
            result.ZombieRunsTerminated = await DetectAndTerminateZombieRunsAsync();
            result.OverdueRunsTerminated = await DetectAndTerminateOverdueRunsAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during dead-run monitoring cycle");
        }
        return result;
    }

    internal async Task<int> DetectAndTerminateStaleRunsAsync()
    {
        var runningRuns = await store.ListRunsByStateAsync(RunState.Running, CancellationToken.None);
        var staleThreshold = DateTime.UtcNow - TimeSpan.FromMinutes(_config.StaleRunThresholdMinutes);
        var terminatedCount = 0;

        foreach (var run in runningRuns)
        {
            var lastActivity = run.StartedAtUtc ?? run.CreatedAtUtc;
            if (lastActivity < staleThreshold)
            {
                logger.LogWarning("Detected stale run {RunId} (last activity: {LastActivity}). Terminating...",
                    run.Id, lastActivity);

                await TerminateRunAsync(run, "Stale run detected - no activity within threshold", "StaleRun");
                terminatedCount++;
            }
        }

        if (terminatedCount > 0)
            logger.LogInformation("Terminated {Count} stale runs", terminatedCount);

        return terminatedCount;
    }

    internal async Task<int> DetectAndTerminateZombieRunsAsync()
    {
        var runningRuns = await store.ListRunsByStateAsync(RunState.Running, CancellationToken.None);
        var zombieThreshold = DateTime.UtcNow - TimeSpan.FromMinutes(_config.ZombieRunThresholdMinutes);
        var terminatedCount = 0;

        foreach (var run in runningRuns)
        {
            var runAge = run.StartedAtUtc ?? run.CreatedAtUtc;
            if (runAge < zombieThreshold)
            {
                logger.LogWarning("Detected zombie run {RunId} (started: {StartedAt}). Force terminating...",
                    run.Id, runAge);

                await TerminateRunAsync(run, "Zombie run detected - exceeded maximum runtime", "ZombieRun", force: true);
                terminatedCount++;
            }
        }

        if (terminatedCount > 0)
            logger.LogInformation("Force terminated {Count} zombie runs", terminatedCount);

        return terminatedCount;
    }

    internal async Task<int> DetectAndTerminateOverdueRunsAsync()
    {
        var runningRuns = await store.ListRunsByStateAsync(RunState.Running, CancellationToken.None);
        var maxAgeThreshold = DateTime.UtcNow - TimeSpan.FromHours(_config.MaxRunAgeHours);
        var terminatedCount = 0;

        foreach (var run in runningRuns)
        {
            var runAge = run.StartedAtUtc ?? run.CreatedAtUtc;
            if (runAge < maxAgeThreshold)
            {
                logger.LogWarning("Detected overdue run {RunId} (started: {StartedAt}, max age: {MaxAge}h). Terminating...",
                    run.Id, runAge, _config.MaxRunAgeHours);

                await TerminateRunAsync(run, "Run exceeded maximum allowed age", "OverdueRun", force: true);
                terminatedCount++;
            }
        }

        if (terminatedCount > 0)
            logger.LogInformation("Terminated {Count} overdue runs", terminatedCount);

        return terminatedCount;
    }

    private async Task TerminateRunAsync(RunDocument run, string reason, string failureClass, bool force = false)
    {
        try
        {
            if (_config.ForceKillOnTimeout && force)
            {
                var killResult = await containerReaper.KillContainerAsync(run.Id, reason, force: true, CancellationToken.None);
                if (killResult.Killed)
                {
                    logger.LogInformation("Container {ContainerId} killed for run {RunId}", killResult.ContainerId, run.Id);
                }
                else
                {
                    logger.LogWarning("Failed to kill container for run {RunId}: {Error}", run.Id, killResult.Error);
                }
            }

            var failed = await store.MarkRunCompletedAsync(
                run.Id,
                succeeded: false,
                summary: reason,
                outputJson: "{}",
                CancellationToken.None,
                failureClass: failureClass);

            if (failed is not null)
            {
                await publisher.PublishStatusAsync(failed, CancellationToken.None);
                await store.CreateFindingFromFailureAsync(failed, $"{reason}. Run {run.Id} was terminated automatically.", CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to terminate run {RunId}", run.Id);
        }
    }

    private async Task RecoverOrphanedRunsAsync(CancellationToken cancellationToken)
    {
        var runningRuns = await store.ListRunsByStateAsync(RunState.Running, cancellationToken);
        var recoveredCount = 0;

        foreach (var run in runningRuns)
        {
            try
            {
                logger.LogWarning("Orphaned running run detected: {RunId}. Marking as failed", run.Id);

                var failed = await store.MarkRunCompletedAsync(
                    run.Id,
                    succeeded: false,
                    summary: "Orphaned run recovered on startup",
                    outputJson: "{}",
                    cancellationToken,
                    failureClass: "OrphanRecovery");

                if (failed is not null)
                {
                    await publisher.PublishStatusAsync(failed, cancellationToken);
                    await store.CreateFindingFromFailureAsync(failed, "Run was still in Running state when control plane restarted. Marked as failed.", cancellationToken);
                    recoveredCount++;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to recover orphaned run {RunId}", run.Id);
            }
        }

        if (recoveredCount > 0)
            logger.LogInformation("Recovery complete: {Count} orphaned runs marked as failed", recoveredCount);
    }

    private async Task RecoverOrphanedWorkflowExecutionsAsync(CancellationToken cancellationToken)
    {
        var runningExecutions = await store.ListWorkflowExecutionsByStateAsync(WorkflowExecutionState.Running, cancellationToken);
        var recoveredCount = 0;

        foreach (var execution in runningExecutions)
        {
            try
            {
                logger.LogWarning("Orphaned workflow execution detected: {ExecutionId} (Workflow: {WorkflowId}). Marking as failed",
                    execution.Id, execution.WorkflowId);

                var failed = await store.MarkWorkflowExecutionCompletedAsync(
                    execution.Id,
                    WorkflowExecutionState.Failed,
                    "Workflow execution was still running when control plane restarted. Marked as failed.",
                    cancellationToken);

                if (failed is not null)
                {
                    recoveredCount++;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to recover orphaned workflow execution {ExecutionId}", execution.Id);
            }
        }

        if (recoveredCount > 0)
            logger.LogInformation("Recovery complete: {Count} orphaned workflow executions marked as failed", recoveredCount);
    }

    private async Task LogPendingApprovalRunsAsync(CancellationToken cancellationToken)
    {
        var pendingRuns = await store.ListRunsByStateAsync(RunState.PendingApproval, cancellationToken);
        if (pendingRuns.Count > 0)
        {
            logger.LogWarning("Found {Count} runs pending approval after restart. These require manual action.", pendingRuns.Count);
            foreach (var run in pendingRuns)
            {
                logger.LogInformation("Run {RunId} is pending approval (Task: {TaskId})", run.Id, run.TaskId);
            }
        }
    }

    private async Task LogQueuedRunsAsync(CancellationToken cancellationToken)
    {
        var queuedRuns = await store.ListRunsByStateAsync(RunState.Queued, cancellationToken);
        if (queuedRuns.Count > 0)
            logger.LogInformation("Recovery found {Count} queued runs that will be picked up by the scheduler", queuedRuns.Count);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Recovery service stopping");
        _monitoringTimer?.Change(Timeout.Infinite, 0);
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        _monitoringTimer?.Dispose();
    }
}

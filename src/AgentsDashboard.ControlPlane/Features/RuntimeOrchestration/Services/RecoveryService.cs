using System.Diagnostics;
using System.Diagnostics.Metrics;



using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Features.RuntimeOrchestration.Services;

public sealed record DeadRunDetectionResult
{
    public int StaleRunsTerminated { get; set; }
    public int ZombieRunsTerminated { get; set; }
    public int OverdueRunsTerminated { get; set; }
}

public sealed class RecoveryService(
    IOrchestratorStore store,
    IRunEventPublisher publisher,
    IContainerReaper containerReaper,
    IOptions<OrchestratorOptions> options,
    IHostApplicationLifetime applicationLifetime,
    IBackgroundWorkCoordinator backgroundWorkCoordinator,
    ILogger<RecoveryService> logger,
    TimeProvider? timeProvider = null) : IHostedService, IDisposable
{
    private static readonly Meter s_meter = new("AgentsDashboard.ControlPlane.Recovery");
    private static readonly Counter<int> s_orphanedContainersDetected = s_meter.CreateCounter<int>("orphaned_containers_detected", "containers");
    private static readonly Counter<int> s_orphanedContainersRemoved = s_meter.CreateCounter<int>("orphaned_containers_removed", "containers");

    private readonly DeadRunDetectionConfig _config = options.Value.DeadRunDetection;
    private readonly StageTimeoutConfig _stageTimeoutConfig = options.Value.StageTimeout;
    private Timer? _monitoringTimer;
    private readonly object _lock = new();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private const string StartupOperationKey = "startup:recovery";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Recovery service starting; queuing startup recovery background work.");

        if (!applicationLifetime.ApplicationStarted.IsCancellationRequested)
        {
            applicationLifetime.ApplicationStarted.Register(() =>
            {
                QueueStartupRecoveryWork();
            });
        }
        else
        {
            QueueStartupRecoveryWork();
        }

        return Task.CompletedTask;
    }

    private void QueueStartupRecoveryWork()
    {
        var workId = backgroundWorkCoordinator.Enqueue(
            BackgroundWorkKind.Recovery,
            StartupOperationKey,
            RunStartupRecoveryAsync,
            dedupeByOperationKey: true,
            isCritical: false);
        logger.LogInformation("Queued startup recovery background work {WorkId}", workId);
    }

    private async Task RunStartupRecoveryAsync(
        CancellationToken cancellationToken,
        IProgress<BackgroundWorkSnapshot> progress)
    {
        try
        {
            progress.Report(CreateProgress("Recovering orphaned runs.", 10));
            await RecoverOrphanedRunsAsync(cancellationToken);
            progress.Report(CreateProgress("Inspecting pending approval runs.", 55));
            await LogPendingApprovalRunsAsync(cancellationToken);
            progress.Report(CreateProgress("Inspecting queued runs.", 70));
            await LogQueuedRunsAsync(cancellationToken);
            progress.Report(CreateProgress("Reconciling orphaned containers.", 85));
            await ReconcileOrphanedContainersAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Recovery startup pass failed");
            progress.Report(new BackgroundWorkSnapshot(
                WorkId: string.Empty,
                OperationKey: string.Empty,
                Kind: BackgroundWorkKind.Recovery,
                State: BackgroundWorkState.Failed,
                PercentComplete: null,
                Message: "Recovery startup pass failed.",
                StartedAt: null,
                UpdatedAt: DateTimeOffset.UtcNow,
                ErrorCode: "recovery_startup_failed",
                ErrorMessage: ex.Message));
            return;
        }

        if (_config.EnableAutoTermination)
        {
            StartDeadRunMonitoring();
        }

        progress.Report(new BackgroundWorkSnapshot(
            WorkId: string.Empty,
            OperationKey: string.Empty,
            Kind: BackgroundWorkKind.Recovery,
            State: BackgroundWorkState.Succeeded,
            PercentComplete: 100,
            Message: "Recovery startup pass completed.",
            StartedAt: null,
            UpdatedAt: DateTimeOffset.UtcNow,
            ErrorCode: null,
            ErrorMessage: null));
    }

    private static BackgroundWorkSnapshot CreateProgress(string message, int? percentComplete)
    {
        return new BackgroundWorkSnapshot(
            WorkId: string.Empty,
            OperationKey: string.Empty,
            Kind: BackgroundWorkKind.Recovery,
            State: BackgroundWorkState.Running,
            PercentComplete: percentComplete,
            Message: message,
            StartedAt: null,
            UpdatedAt: DateTimeOffset.UtcNow,
            ErrorCode: null,
            ErrorMessage: null);
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

    public async Task<DeadRunDetectionResult> MonitorForDeadRunsAsync()
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

    public async Task<int> DetectAndTerminateStaleRunsAsync()
    {
        var runningRuns = await store.ListRunsByStateAsync(RunState.Running, CancellationToken.None);
        var staleThreshold = _timeProvider.GetUtcNow().UtcDateTime - TimeSpan.FromMinutes(_config.StaleRunThresholdMinutes);
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

    public async Task<int> DetectAndTerminateZombieRunsAsync()
    {
        var runningRuns = await store.ListRunsByStateAsync(RunState.Running, CancellationToken.None);
        var zombieThreshold = _timeProvider.GetUtcNow().UtcDateTime - TimeSpan.FromMinutes(_config.ZombieRunThresholdMinutes);
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

    public async Task<int> DetectAndTerminateOverdueRunsAsync()
    {
        var runningRuns = await store.ListRunsByStateAsync(RunState.Running, CancellationToken.None);
        var maxAgeThreshold = _timeProvider.GetUtcNow().UtcDateTime - TimeSpan.FromHours(_config.MaxRunAgeHours);
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

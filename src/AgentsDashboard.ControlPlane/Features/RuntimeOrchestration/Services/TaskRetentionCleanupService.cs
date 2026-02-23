


namespace AgentsDashboard.ControlPlane.Features.RuntimeOrchestration.Services;

public sealed record TaskCleanupRunSummary(
    bool Executed,
    bool LeaseAcquired,
    bool AgeCleanupApplied,
    bool SizeCleanupApplied,
    bool VacuumExecuted,
    long InitialBytes,
    long FinalBytes,
    int TasksDeleted,
    int FailedTasks,
    int DeletedRows,
    string Reason);

public sealed class TaskRetentionCleanupService(
    IOrchestratorStore store,
    ILeaseCoordinator leaseCoordinator,
    ILogger<TaskRetentionCleanupService> logger,
    TimeProvider? timeProvider = null) : BackgroundService
{
    private const string CleanupLeaseName = "maintenance-task-cleanup";

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromMinutes(10);

            try
            {
                var settingsDocument = await store.GetSettingsAsync(stoppingToken);
                var settings = settingsDocument.Orchestrator ?? new OrchestratorSettings();
                delay = ResolveCleanupInterval(settings.CleanupIntervalMinutes);
                await RunCleanupCycleAsync(settings, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Task cleanup cycle failed");
            }

            await Task.Delay(delay, stoppingToken);
        }
    }

    public async Task<TaskCleanupRunSummary> RunCleanupCycleAsync(CancellationToken cancellationToken)
    {
        var settingsDocument = await store.GetSettingsAsync(cancellationToken);
        return await RunCleanupCycleAsync(settingsDocument.Orchestrator ?? new OrchestratorSettings(), cancellationToken);
    }

    public async Task<TaskCleanupRunSummary> RunCleanupCycleAsync(OrchestratorSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.EnableTaskAutoCleanup)
        {
            return new TaskCleanupRunSummary(
                Executed: false,
                LeaseAcquired: false,
                AgeCleanupApplied: false,
                SizeCleanupApplied: false,
                VacuumExecuted: false,
                InitialBytes: 0,
                FinalBytes: 0,
                TasksDeleted: 0,
                FailedTasks: 0,
                DeletedRows: 0,
                Reason: "disabled");
        }

        var leaseTtl = ResolveLeaseTtl(settings.CleanupIntervalMinutes);
        await using var lease = await leaseCoordinator.TryAcquireAsync(CleanupLeaseName, leaseTtl, cancellationToken);
        if (lease is null)
        {
            return new TaskCleanupRunSummary(
                Executed: false,
                LeaseAcquired: false,
                AgeCleanupApplied: false,
                SizeCleanupApplied: false,
                VacuumExecuted: false,
                InitialBytes: 0,
                FinalBytes: 0,
                TasksDeleted: 0,
                FailedTasks: 0,
                DeletedRows: 0,
                Reason: "lease-unavailable");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var protectedDays = Math.Max(0, settings.CleanupProtectedDays);
        var retentionDays = Math.Max(Math.Max(1, settings.TaskRetentionDays), protectedDays);
        var disabledTaskInactivityDays = Math.Max(0, settings.DisabledTaskInactivityDays);
        var includeDisabledInactiveEligibility = disabledTaskInactivityDays > 0;
        var maxTasksPerTick = Math.Clamp(settings.MaxTasksDeletedPerTick, 1, 2000);
        var protectedSinceUtc = nowUtc.AddDays(-protectedDays);
        var ageOlderThanUtc = nowUtc.AddDays(-retentionDays);
        var disabledInactiveOlderThanUtc = includeDisabledInactiveEligibility
            ? nowUtc.AddDays(-disabledTaskInactivityDays)
            : default;

        var softLimitGb = Math.Max(1, settings.DbSizeSoftLimitGb);
        var targetGb = settings.DbSizeTargetGb;
        if (targetGb <= 0 || targetGb >= softLimitGb)
        {
            targetGb = Math.Max(1, softLimitGb - 10);
        }
        if (targetGb >= softLimitGb)
        {
            targetGb = Math.Max(1, softLimitGb - 1);
        }

        var softLimitBytes = ToBytes(softLimitGb);
        var targetBytes = ToBytes(targetGb);

        var initialSnapshot = await store.GetStorageSnapshotAsync(cancellationToken);
        var remainingDeleteBudget = maxTasksPerTick;
        var ageCleanupApplied = false;
        var sizeCleanupApplied = false;
        var totalTasksDeleted = 0;
        var totalFailedTasks = 0;
        var totalDeletedRows = 0;
        var structuredPruneBatchSize = Math.Max(100, maxTasksPerTick * 50);
        var structuredPrune = await store.PruneStructuredRunDataAsync(
            ageOlderThanUtc,
            structuredPruneBatchSize,
            settings.CleanupExcludeTasksWithOpenFindings,
            cancellationToken);
        totalDeletedRows += CountDeletedRows(structuredPrune);

        var ageCandidates = await store.ListTaskCleanupCandidatesAsync(
            new TaskCleanupQuery(
                OlderThanUtc: ageOlderThanUtc,
                ProtectedSinceUtc: protectedSinceUtc,
                Limit: remainingDeleteBudget,
                OnlyWithNoActiveRuns: true,
                RepositoryId: null,
                ScanLimit: Math.Max(remainingDeleteBudget * 20, 200),
                IncludeRetentionEligibility: true,
                IncludeDisabledInactiveEligibility: includeDisabledInactiveEligibility,
                DisabledInactiveOlderThanUtc: disabledInactiveOlderThanUtc,
                ExcludeTasksWithOpenFindings: settings.CleanupExcludeTasksWithOpenFindings),
            cancellationToken);

        if (ageCandidates.Count > 0)
        {
            var ageBatch = await store.DeleteTasksCascadeAsync(ageCandidates.Select(x => x.TaskId).ToList(), cancellationToken);
            ageCleanupApplied = ageBatch.TasksDeleted > 0 || ageBatch.FailedTasks > 0;
            remainingDeleteBudget = Math.Max(0, remainingDeleteBudget - ageBatch.TasksDeleted - ageBatch.FailedTasks);
            totalTasksDeleted += ageBatch.TasksDeleted;
            totalFailedTasks += ageBatch.FailedTasks;
            totalDeletedRows += CountDeletedRows(ageBatch);
        }

        var currentSnapshot = await store.GetStorageSnapshotAsync(cancellationToken);
        if (currentSnapshot.TotalBytes > softLimitBytes && remainingDeleteBudget > 0)
        {
            while (remainingDeleteBudget > 0 && currentSnapshot.TotalBytes > targetBytes)
            {
                var batchSize = Math.Min(25, remainingDeleteBudget);
                var pressureCandidates = await store.ListTaskCleanupCandidatesAsync(
                    new TaskCleanupQuery(
                        OlderThanUtc: nowUtc,
                        ProtectedSinceUtc: protectedSinceUtc,
                        Limit: batchSize,
                        OnlyWithNoActiveRuns: true,
                        RepositoryId: null,
                        ScanLimit: Math.Max(batchSize * 20, 200),
                        IncludeRetentionEligibility: true,
                        IncludeDisabledInactiveEligibility: includeDisabledInactiveEligibility,
                        DisabledInactiveOlderThanUtc: disabledInactiveOlderThanUtc,
                        ExcludeTasksWithOpenFindings: settings.CleanupExcludeTasksWithOpenFindings),
                    cancellationToken);

                if (pressureCandidates.Count == 0)
                {
                    break;
                }

                var pressureBatch = await store.DeleteTasksCascadeAsync(pressureCandidates.Select(x => x.TaskId).ToList(), cancellationToken);
                sizeCleanupApplied = pressureBatch.TasksDeleted > 0 || pressureBatch.FailedTasks > 0;
                remainingDeleteBudget = Math.Max(0, remainingDeleteBudget - pressureBatch.TasksDeleted - pressureBatch.FailedTasks);
                totalTasksDeleted += pressureBatch.TasksDeleted;
                totalFailedTasks += pressureBatch.FailedTasks;
                totalDeletedRows += CountDeletedRows(pressureBatch);

                if (pressureBatch.TasksDeleted == 0 && pressureBatch.FailedTasks == 0)
                {
                    break;
                }

                currentSnapshot = await store.GetStorageSnapshotAsync(cancellationToken);
            }
        }

        var vacuumExecuted = false;
        if (sizeCleanupApplied &&
            settings.EnableVacuumAfterPressureCleanup &&
            totalDeletedRows >= Math.Max(1, settings.VacuumMinDeletedRows))
        {
            await store.VacuumAsync(cancellationToken);
            vacuumExecuted = true;
            currentSnapshot = await store.GetStorageSnapshotAsync(cancellationToken);
        }

        var summary = new TaskCleanupRunSummary(
            Executed: true,
            LeaseAcquired: true,
            AgeCleanupApplied: ageCleanupApplied,
            SizeCleanupApplied: sizeCleanupApplied,
            VacuumExecuted: vacuumExecuted,
            InitialBytes: initialSnapshot.TotalBytes,
            FinalBytes: currentSnapshot.TotalBytes,
            TasksDeleted: totalTasksDeleted,
            FailedTasks: totalFailedTasks,
            DeletedRows: totalDeletedRows,
            Reason: BuildReason(ageCleanupApplied, sizeCleanupApplied, totalTasksDeleted, totalFailedTasks));

        if (summary.Reason == "no-op")
        {
            logger.LogDebug(
                "Task cleanup cycle completed: reason={Reason}, tasksDeleted={TasksDeleted}, failedTasks={FailedTasks}, initialBytes={InitialBytes}, finalBytes={FinalBytes}, vacuum={VacuumExecuted}",
                summary.Reason,
                summary.TasksDeleted,
                summary.FailedTasks,
                summary.InitialBytes,
                summary.FinalBytes,
                summary.VacuumExecuted);
            return summary;
        }

        logger.LogInformation(
            "Task cleanup cycle completed: reason={Reason}, tasksDeleted={TasksDeleted}, failedTasks={FailedTasks}, initialBytes={InitialBytes}, finalBytes={FinalBytes}, vacuum={VacuumExecuted}",
            summary.Reason,
            summary.TasksDeleted,
            summary.FailedTasks,
            summary.InitialBytes,
            summary.FinalBytes,
            summary.VacuumExecuted);

        return summary;
    }

    private static int CountDeletedRows(CleanupBatchResult batch)
    {
        return batch.TasksDeleted +
               batch.DeletedRuns +
               batch.DeletedRunLogs +
               batch.DeletedFindings +
               batch.DeletedPromptEntries +
               batch.DeletedRunSummaries +
               batch.DeletedSemanticChunks;
    }

    private static int CountDeletedRows(StructuredRunDataPruneResult batch)
    {
        return batch.DeletedStructuredEvents +
               batch.DeletedDiffSnapshots +
               batch.DeletedToolProjections;
    }

    private static long ToBytes(int gigabytes)
    {
        return gigabytes <= 0
            ? 0
            : gigabytes * 1024L * 1024L * 1024L;
    }

    private static TimeSpan ResolveCleanupInterval(int cleanupIntervalMinutes)
    {
        return TimeSpan.FromMinutes(Math.Clamp(cleanupIntervalMinutes, 1, 1440));
    }

    private static TimeSpan ResolveLeaseTtl(int cleanupIntervalMinutes)
    {
        var intervalMinutes = Math.Clamp(cleanupIntervalMinutes, 1, 1440);
        return TimeSpan.FromMinutes(Math.Max(2, intervalMinutes * 2));
    }

    private static string BuildReason(bool ageCleanupApplied, bool sizeCleanupApplied, int tasksDeleted, int failedTasks)
    {
        if (tasksDeleted == 0 && failedTasks == 0)
        {
            return "no-op";
        }

        if (ageCleanupApplied && sizeCleanupApplied)
        {
            return "age-and-size";
        }

        if (sizeCleanupApplied)
        {
            return "size-only";
        }

        return "age-only";
    }
}

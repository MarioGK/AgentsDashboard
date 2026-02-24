namespace AgentsDashboard.ControlPlane.Infrastructure.Data;

public sealed class RuntimeStore(
    IOrchestratorRepositorySessionFactory liteDbScopeFactory) : IRuntimeStore
{

    public async Task<List<TaskRuntimeRegistration>> ListTaskRuntimeRegistrationsAsync(CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.TaskRuntimeRegistrations.AsNoTracking().OrderBy(x => x.RuntimeId).ToListAsync(cancellationToken);
    }

    public async Task UpsertTaskRuntimeRegistrationHeartbeatAsync(string runtimeId, string endpoint, int activeSlots, int maxSlots, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var registration = await db.TaskRuntimeRegistrations.FirstOrDefaultAsync(x => x.RuntimeId == runtimeId, cancellationToken);
        if (registration is null)
        {
            registration = new TaskRuntimeRegistration
            {
                RuntimeId = runtimeId,
                RegisteredAtUtc = DateTime.UtcNow,
            };
            db.TaskRuntimeRegistrations.Add(registration);
        }

        registration.Endpoint = endpoint;
        registration.ActiveSlots = Math.Max(0, activeSlots);
        registration.MaxSlots = maxSlots > 0 ? maxSlots : Math.Max(1, registration.MaxSlots);
        registration.Online = true;
        registration.LastHeartbeatUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkStaleTaskRuntimeRegistrationsOfflineAsync(TimeSpan threshold, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var cutoff = DateTime.UtcNow - threshold;
        var stale = await db.TaskRuntimeRegistrations
            .Where(x => x.Online && x.LastHeartbeatUtc < cutoff)
            .ToListAsync(cancellationToken);
        if (stale.Count == 0)
        {
            return;
        }

        foreach (var registration in stale)
        {
            registration.Online = false;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> PruneOfflineTaskRuntimeRegistrationsAsync(
        TimeSpan threshold,
        IReadOnlyCollection<string> activeRuntimeIds,
        CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var cutoff = DateTime.UtcNow - threshold;
        var active = activeRuntimeIds.Count == 0
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : activeRuntimeIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stale = await db.TaskRuntimeRegistrations
            .Where(x => !x.Online && x.LastHeartbeatUtc < cutoff)
            .ToListAsync(cancellationToken);
        if (stale.Count == 0)
        {
            return 0;
        }

        var removed = 0;
        foreach (var registration in stale)
        {
            if (active.Contains(registration.RuntimeId))
            {
                continue;
            }

            db.TaskRuntimeRegistrations.Remove(registration);
            removed++;
        }

        if (removed == 0)
        {
            return 0;
        }

        await db.SaveChangesAsync(cancellationToken);
        return removed;
    }

    public async Task<List<TaskRuntimeDocument>> ListTaskRuntimesAsync(CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.TaskRuntimes.AsNoTracking().OrderBy(x => x.RuntimeId).ToListAsync(cancellationToken);
    }

    public async Task<TaskRuntimeDocument> UpsertTaskRuntimeStateAsync(TaskRuntimeStateUpdate update, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(update.RuntimeId))
        {
            throw new InvalidOperationException("RuntimeId is required.");
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var now = update.ObservedAtUtc == default ? DateTime.UtcNow : update.ObservedAtUtc;
        var runtime = await db.TaskRuntimes.FirstOrDefaultAsync(x => x.RuntimeId == update.RuntimeId, cancellationToken);
        if (runtime is null)
        {
            runtime = new TaskRuntimeDocument
            {
                RuntimeId = update.RuntimeId,
                RepositoryId = update.RepositoryId,
                TaskId = update.TaskId,
                LastActivityUtc = now,
                LastStateChangeUtc = now,
            };
            db.TaskRuntimes.Add(runtime);
        }

        var previousState = runtime.State;
        var previousLastActivityUtc = runtime.LastActivityUtc;

        runtime.RepositoryId = string.IsNullOrWhiteSpace(update.RepositoryId) ? runtime.RepositoryId : update.RepositoryId;
        runtime.TaskId = string.IsNullOrWhiteSpace(update.TaskId) ? runtime.TaskId : update.TaskId;
        runtime.State = update.State;
        runtime.ActiveRuns = Math.Max(0, update.ActiveRuns);
        runtime.MaxParallelRuns = update.MaxParallelRuns > 0 ? update.MaxParallelRuns : Math.Max(1, runtime.MaxParallelRuns);
        runtime.Endpoint = string.IsNullOrWhiteSpace(update.Endpoint) ? runtime.Endpoint : update.Endpoint;
        runtime.ContainerId = string.IsNullOrWhiteSpace(update.ContainerId) ? runtime.ContainerId : update.ContainerId;
        runtime.WorkspacePath = string.IsNullOrWhiteSpace(update.WorkspacePath) ? runtime.WorkspacePath : update.WorkspacePath;
        runtime.RuntimeHomePath = string.IsNullOrWhiteSpace(update.RuntimeHomePath) ? runtime.RuntimeHomePath : update.RuntimeHomePath;
        if (previousState != runtime.State || runtime.LastStateChangeUtc is null)
        {
            runtime.LastStateChangeUtc = now;
        }

        if (update.UpdateLastActivityUtc)
        {
            runtime.LastActivityUtc = now;
        }

        if (!string.IsNullOrWhiteSpace(update.LastError))
        {
            runtime.LastError = update.LastError.Trim();
        }
        else if (runtime.State != TaskRuntimeState.Failed)
        {
            runtime.LastError = string.Empty;
        }

        if (update.ClearInactiveAfterUtc)
        {
            runtime.InactiveAfterUtc = null;
        }
        else if (update.InactiveAfterUtc.HasValue)
        {
            runtime.InactiveAfterUtc = update.InactiveAfterUtc.Value;
        }
        else if (runtime.State == TaskRuntimeState.Inactive)
        {
            runtime.InactiveAfterUtc = now;
        }

        if (runtime.State == TaskRuntimeState.Starting)
        {
            runtime.LastStartedAtUtc = now;
        }

        if (runtime.State == TaskRuntimeState.Ready)
        {
            runtime.LastReadyAtUtc = now;
            if (previousState is TaskRuntimeState.Cold or TaskRuntimeState.Starting &&
                runtime.LastStartedAtUtc.HasValue &&
                now >= runtime.LastStartedAtUtc.Value)
            {
                var durationMs = Math.Max(0L, (long)(now - runtime.LastStartedAtUtc.Value).TotalMilliseconds);
                runtime.ColdStartCount++;
                runtime.ColdStartDurationTotalMs += durationMs;
                runtime.LastColdStartDurationMs = durationMs;
            }
        }

        if (runtime.State == TaskRuntimeState.Inactive &&
            previousState != TaskRuntimeState.Inactive &&
            previousLastActivityUtc != default &&
            now >= previousLastActivityUtc)
        {
            var inactiveDurationMs = Math.Max(0L, (long)(now - previousLastActivityUtc).TotalMilliseconds);
            runtime.LastBecameInactiveUtc = now;
            runtime.InactiveTransitionCount++;
            runtime.InactiveDurationTotalMs += inactiveDurationMs;
            runtime.LastInactiveDurationMs = inactiveDurationMs;
        }

        await db.SaveChangesAsync(cancellationToken);
        return runtime;
    }

    public async Task<TaskRuntimeTelemetrySnapshot> GetTaskRuntimeTelemetryAsync(CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var runtimes = await db.TaskRuntimes.AsNoTracking().ToListAsync(cancellationToken);
        if (runtimes.Count == 0)
        {
            return new TaskRuntimeTelemetrySnapshot(
                TotalRuntimes: 0,
                ReadyRuntimes: 0,
                BusyRuntimes: 0,
                InactiveRuntimes: 0,
                FailedRuntimes: 0,
                TotalColdStarts: 0,
                AverageColdStartSeconds: 0,
                LastColdStartSeconds: 0,
                TotalInactiveTransitions: 0,
                AverageInactiveSeconds: 0,
                LastInactiveSeconds: 0);
        }

        var totalColdStarts = runtimes.Sum(x => x.ColdStartCount);
        var totalColdStartDurationMs = runtimes.Sum(x => x.ColdStartDurationTotalMs);
        var totalInactiveTransitions = runtimes.Sum(x => x.InactiveTransitionCount);
        var totalInactiveDurationMs = runtimes.Sum(x => x.InactiveDurationTotalMs);
        var lastColdStartSeconds = runtimes.Where(x => x.LastColdStartDurationMs > 0)
            .Select(x => x.LastColdStartDurationMs / 1000d)
            .DefaultIfEmpty(0)
            .Average();
        var lastInactiveSeconds = runtimes.Where(x => x.LastInactiveDurationMs > 0)
            .Select(x => x.LastInactiveDurationMs / 1000d)
            .DefaultIfEmpty(0)
            .Average();

        return new TaskRuntimeTelemetrySnapshot(
            TotalRuntimes: runtimes.Count,
            ReadyRuntimes: runtimes.Count(x => x.State == TaskRuntimeState.Ready),
            BusyRuntimes: runtimes.Count(x => x.State == TaskRuntimeState.Busy),
            InactiveRuntimes: runtimes.Count(x => x.State == TaskRuntimeState.Inactive),
            FailedRuntimes: runtimes.Count(x => x.State == TaskRuntimeState.Failed),
            TotalColdStarts: totalColdStarts,
            AverageColdStartSeconds: totalColdStarts > 0 ? totalColdStartDurationMs / 1000d / totalColdStarts : 0,
            LastColdStartSeconds: lastColdStartSeconds,
            TotalInactiveTransitions: totalInactiveTransitions,
            AverageInactiveSeconds: totalInactiveTransitions > 0 ? totalInactiveDurationMs / 1000d / totalInactiveTransitions : 0,
            LastInactiveSeconds: lastInactiveSeconds);
    }

    public async Task<TaskRuntimeEventCheckpointDocument?> GetTaskRuntimeEventCheckpointAsync(string runtimeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runtimeId))
        {
            return null;
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.TaskRuntimeEventCheckpoints.AsNoTracking()
            .FirstOrDefaultAsync(x => x.RuntimeId == runtimeId, cancellationToken);
    }

    public async Task<TaskRuntimeEventCheckpointDocument> UpsertTaskRuntimeEventCheckpointAsync(
        string runtimeId,
        long lastDeliveryId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runtimeId))
        {
            throw new ArgumentException("Runtime id is required.", nameof(runtimeId));
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var normalizedRuntimeId = runtimeId.Trim();
        var checkpoint = await db.TaskRuntimeEventCheckpoints
            .FirstOrDefaultAsync(x => x.RuntimeId == normalizedRuntimeId, cancellationToken);
        if (checkpoint is null)
        {
            checkpoint = new TaskRuntimeEventCheckpointDocument
            {
                Id = normalizedRuntimeId,
                RuntimeId = normalizedRuntimeId,
                LastDeliveryId = Math.Max(0, lastDeliveryId),
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.TaskRuntimeEventCheckpoints.Add(checkpoint);
            await db.SaveChangesAsync(cancellationToken);
            return checkpoint;
        }

        checkpoint.LastDeliveryId = Math.Max(checkpoint.LastDeliveryId, lastDeliveryId);
        checkpoint.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return checkpoint;
    }


    public async Task<bool> TryAcquireLeaseAsync(string leaseName, string ownerId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var expiresAtUtc = now.Add(ttl);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lease = await db.Leases.FirstOrDefaultAsync(x => x.LeaseName == leaseName, cancellationToken);
        if (lease is null)
        {
            db.Leases.Add(new OrchestratorLeaseDocument
            {
                LeaseName = leaseName,
                OwnerId = ownerId,
                ExpiresAtUtc = expiresAtUtc,
            });

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        if (string.Equals(lease.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase) || lease.ExpiresAtUtc <= now)
        {
            lease.OwnerId = ownerId;
            lease.ExpiresAtUtc = expiresAtUtc;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        await transaction.RollbackAsync(cancellationToken);
        return false;
    }

    public async Task<bool> RenewLeaseAsync(string leaseName, string ownerId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var lease = await db.Leases.FirstOrDefaultAsync(
            x => x.LeaseName == leaseName && x.OwnerId == ownerId,
            cancellationToken);

        if (lease is null)
        {
            return false;
        }

        lease.ExpiresAtUtc = DateTime.UtcNow.Add(ttl);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task ReleaseLeaseAsync(string leaseName, string ownerId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var lease = await db.Leases.FirstOrDefaultAsync(
            x => x.LeaseName == leaseName && x.OwnerId == ownerId,
            cancellationToken);

        if (lease is null)
        {
            return;
        }

        db.Leases.Remove(lease);
        await db.SaveChangesAsync(cancellationToken);
    }


    private static DateTime MaxDateTime(params DateTime?[] values)
    {
        var max = DateTime.MinValue;
        foreach (var value in values)
        {
            if (!value.HasValue)
            {
                continue;
            }

            if (value.Value > max)
            {
                max = value.Value;
            }
        }

        return max == DateTime.MinValue ? DateTime.UtcNow : max;
    }


}

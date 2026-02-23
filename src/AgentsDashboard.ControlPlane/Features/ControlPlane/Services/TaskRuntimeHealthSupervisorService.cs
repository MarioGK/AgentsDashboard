using System.Collections.Concurrent;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class TaskRuntimeHealthSupervisorService(
    ITaskRuntimeLifecycleManager lifecycleManager,
    IOrchestratorRuntimeSettingsProvider runtimeSettingsProvider,
    IOrchestratorStore store,
    INotificationService notificationService,
    IMagicOnionClientFactory clientFactory,
    ILogger<TaskRuntimeHealthSupervisorService> logger) : BackgroundService
{
    private const int MaxIncidents = 200;
    private static readonly TimeSpan StateRetention = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, RuntimeHealthState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<TaskRuntimeHealthIncident> _incidents = [];
    private readonly object _incidentsLock = new();
    private readonly object _snapshotLock = new();
    private TaskRuntimeHealthSnapshot _snapshot = TaskRuntimeHealthSnapshot.Empty;
    private DateTime? _readinessBlockedSinceUtc;
    private DateTime? _lastRemediationAtUtc;

    public TaskRuntimeHealthSnapshot GetSnapshot()
    {
        lock (_snapshotLock)
        {
            return _snapshot;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var probeInterval = TimeSpan.FromSeconds(10);
            try
            {
                var runtimeSettings = await runtimeSettingsProvider.GetAsync(stoppingToken);
                probeInterval = TimeSpan.FromSeconds(Math.Max(1, runtimeSettings.HealthProbeIntervalSeconds));
                await RunCycleAsync(runtimeSettings, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Task runtime health supervision cycle failed");
            }

            await Task.Delay(probeInterval, stoppingToken);
        }
    }

    private async Task RunCycleAsync(OrchestratorRuntimeSettings runtimeSettings, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var workers = await lifecycleManager.ListTaskRuntimesAsync(cancellationToken);
        var registrations = await store.ListTaskRuntimeRegistrationsAsync(cancellationToken);
        var registrationsByRuntimeId = new Dictionary<string, TaskRuntimeRegistration>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in registrations)
        {
            registrationsByRuntimeId[registration.RuntimeId] = registration;
        }

        foreach (var worker in workers.Where(x => x.IsRunning))
        {
            registrationsByRuntimeId.TryGetValue(worker.TaskRuntimeId, out var registration);
            await ProbeRuntimeAsync(worker, registration, runtimeSettings, now, cancellationToken);
        }

        var runningIds = workers
            .Where(x => x.IsRunning)
            .Select(x => x.TaskRuntimeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var worker in workers.Where(x => !x.IsRunning))
        {
            registrationsByRuntimeId.TryGetValue(worker.TaskRuntimeId, out var registration);
            UpdateOfflineRuntime(worker.TaskRuntimeId, worker.GrpcEndpoint, worker.ActiveSlots, worker.MaxSlots, registration, now);
        }

        foreach (var registration in registrations.Where(x => !runningIds.Contains(x.RuntimeId)))
        {
            UpdateOfflineRuntime(
                registration.RuntimeId,
                registration.Endpoint,
                registration.ActiveSlots,
                registration.MaxSlots,
                registration,
                now);
        }

        PruneState(now, runningIds, registrationsByRuntimeId.Keys);
        PublishSnapshot(now, runtimeSettings);
    }

    private async Task ProbeRuntimeAsync(
        TaskRuntimeInstance worker,
        TaskRuntimeRegistration? registration,
        OrchestratorRuntimeSettings runtimeSettings,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var state = _states.GetOrAdd(worker.TaskRuntimeId, key => RuntimeHealthState.Create(key));
        state.Endpoint = worker.GrpcEndpoint;
        state.ActiveSlots = worker.ActiveSlots;
        state.MaxSlots = worker.MaxSlots;
        state.LastSeenUtc = now;
        state.IsRunning = true;
        state.IsOnline = registration?.Online ?? false;
        state.LastHeartbeatUtc = registration?.LastHeartbeatUtc;

        var heartbeatStaleAfter = TimeSpan.FromSeconds(Math.Max(10, runtimeSettings.RuntimeHeartbeatStaleSeconds));
        var heartbeatHealthy = registration is not null &&
                              registration.Online &&
                              now - registration.LastHeartbeatUtc <= heartbeatStaleAfter;

        var probeHealthy = false;
        string probeError;
        try
        {
            var client = clientFactory.CreateTaskRuntimeService(worker.TaskRuntimeId, worker.GrpcEndpoint);
            var result = await client.WithCancellationToken(cancellationToken).CheckHealthAsync();
            probeHealthy = result.Success;
            probeError = result.ErrorMessage ?? string.Empty;
        }
        catch (Exception ex)
        {
            probeError = ex.Message;
        }

        state.LastProbeUtc = now;
        if (heartbeatHealthy && probeHealthy)
        {
            var previousStatus = state.Status;
            state.Status = TaskRuntimeHealthStatus.Healthy;
            state.LastReason = "ok";
            state.ConsecutiveProbeFailures = 0;
            state.RestartAttempts = 0;
            state.LastHealthyUtc = now;
            state.Quarantined = false;
            state.RecentRemediationFailures = 0;
            if (previousStatus is not TaskRuntimeHealthStatus.Healthy && previousStatus is not TaskRuntimeHealthStatus.Offline)
            {
                var message = $"Task runtime {worker.TaskRuntimeId} recovered";
                AddIncident(worker.TaskRuntimeId, TaskRuntimeHealthStatus.Healthy, "recovered", "recover", true, message);
                await notificationService.PublishAsync(
                    title: "Task runtime recovered",
                    message: message,
                    severity: NotificationSeverity.Success,
                    source: NotificationSource.System,
                    correlationId: worker.TaskRuntimeId);
            }

            return;
        }

        if (!probeHealthy)
        {
            state.ConsecutiveProbeFailures++;
        }
        else
        {
            state.ConsecutiveProbeFailures = 0;
        }

        var previous = state.Status;
        var reason = ResolveUnhealthyReason(now, registration, heartbeatHealthy, probeHealthy, probeError, heartbeatStaleAfter);
        state.LastReason = reason;

        var isThresholdReached = !heartbeatHealthy || state.ConsecutiveProbeFailures >= Math.Max(1, runtimeSettings.RuntimeProbeFailureThreshold);
        var nextStatus = isThresholdReached ? TaskRuntimeHealthStatus.Unhealthy : TaskRuntimeHealthStatus.Degraded;
        if (state.Quarantined)
        {
            nextStatus = TaskRuntimeHealthStatus.Quarantined;
        }

        state.Status = nextStatus;
        if (previous != nextStatus && nextStatus is TaskRuntimeHealthStatus.Degraded or TaskRuntimeHealthStatus.Unhealthy or TaskRuntimeHealthStatus.Quarantined)
        {
            AddIncident(worker.TaskRuntimeId, nextStatus, reason, "detect", false, $"Task runtime {worker.TaskRuntimeId} marked {nextStatus}");
        }

        if (isThresholdReached && !state.Quarantined)
        {
            await RemediateAsync(state, runtimeSettings, now, cancellationToken);
        }
    }

    private async Task RemediateAsync(
        RuntimeHealthState state,
        OrchestratorRuntimeSettings runtimeSettings,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var cooldown = TimeSpan.FromSeconds(Math.Max(5, runtimeSettings.RuntimeRemediationCooldownSeconds));
        if (state.LastRemediationUtc.HasValue && now - state.LastRemediationUtc.Value < cooldown)
        {
            return;
        }

        var restartLimit = Math.Max(0, runtimeSettings.ContainerRestartLimit);
        if (restartLimit > 0 && state.RestartAttempts < restartLimit)
        {
            state.RestartAttempts++;
            var restartSucceeded = await RestartRuntimeAsync(state, now, cancellationToken);
            if (restartSucceeded)
            {
                state.Status = TaskRuntimeHealthStatus.Recovering;
                return;
            }

            if (state.RestartAttempts < restartLimit)
            {
                return;
            }
        }

        await ApplyUnhealthyActionAsync(state, runtimeSettings, now, cancellationToken);
    }

    private async Task<bool> RestartRuntimeAsync(RuntimeHealthState state, DateTime now, CancellationToken cancellationToken)
    {
        var success = false;
        string error = string.Empty;
        try
        {
            success = await lifecycleManager.RestartTaskRuntimeAsync(state.RuntimeId, cancellationToken);
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        state.LastRemediationUtc = now;
        _lastRemediationAtUtc = now;
        var message = success
            ? $"Restart requested for runtime {state.RuntimeId}"
            : $"Restart failed for runtime {state.RuntimeId}: {error}";

        AddIncident(
            state.RuntimeId,
            success ? TaskRuntimeHealthStatus.Recovering : TaskRuntimeHealthStatus.Unhealthy,
            state.LastReason,
            "restart",
            success,
            message);

        if (success)
        {
            await notificationService.PublishAsync(
                title: "Task runtime restart requested",
                message: message,
                severity: NotificationSeverity.Warning,
                source: NotificationSource.System,
                correlationId: state.RuntimeId);
            return true;
        }

        state.RecentRemediationFailures++;
        await notificationService.PublishAsync(
            title: "Task runtime restart failed",
            message: message,
            severity: NotificationSeverity.Error,
            source: NotificationSource.System,
            correlationId: state.RuntimeId);
        return false;
    }

    private async Task ApplyUnhealthyActionAsync(
        RuntimeHealthState state,
        OrchestratorRuntimeSettings runtimeSettings,
        DateTime now,
        CancellationToken cancellationToken)
    {
        state.LastRemediationUtc = now;
        _lastRemediationAtUtc = now;

        switch (runtimeSettings.ContainerUnhealthyAction)
        {
            case ContainerUnhealthyAction.Restart:
            {
                var restartSucceeded = await RestartRuntimeAsync(state, now, cancellationToken);
                if (restartSucceeded)
                {
                    state.Status = TaskRuntimeHealthStatus.Recovering;
                }

                break;
            }
            case ContainerUnhealthyAction.Recreate:
            {
                var success = false;
                string error = string.Empty;
                try
                {
                    await lifecycleManager.RecycleTaskRuntimeAsync(state.RuntimeId, cancellationToken);
                    state.RestartAttempts = 0;
                    success = true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                var message = success
                    ? $"Recreate requested for runtime {state.RuntimeId}"
                    : $"Recreate failed for runtime {state.RuntimeId}: {error}";
                AddIncident(
                    state.RuntimeId,
                    success ? TaskRuntimeHealthStatus.Recovering : TaskRuntimeHealthStatus.Unhealthy,
                    state.LastReason,
                    "recreate",
                    success,
                    message);

                if (success)
                {
                    state.Status = TaskRuntimeHealthStatus.Recovering;
                    await notificationService.PublishAsync(
                        title: "Task runtime recreate requested",
                        message: message,
                        severity: NotificationSeverity.Warning,
                        source: NotificationSource.System,
                        correlationId: state.RuntimeId);
                }
                else
                {
                    state.RecentRemediationFailures++;
                    await notificationService.PublishAsync(
                        title: "Task runtime recreate failed",
                        message: message,
                        severity: NotificationSeverity.Error,
                        source: NotificationSource.System,
                        correlationId: state.RuntimeId);
                }

                break;
            }
            case ContainerUnhealthyAction.Quarantine:
            {
                var success = false;
                string error = string.Empty;
                try
                {
                    await lifecycleManager.SetTaskRuntimeDrainingAsync(state.RuntimeId, true, cancellationToken);
                    success = true;
                    state.Quarantined = true;
                    state.Status = TaskRuntimeHealthStatus.Quarantined;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                var message = success
                    ? $"Runtime {state.RuntimeId} quarantined"
                    : $"Failed to quarantine runtime {state.RuntimeId}: {error}";
                AddIncident(
                    state.RuntimeId,
                    success ? TaskRuntimeHealthStatus.Quarantined : TaskRuntimeHealthStatus.Unhealthy,
                    state.LastReason,
                    "quarantine",
                    success,
                    message);

                if (success)
                {
                    await notificationService.PublishAsync(
                        title: "Task runtime quarantined",
                        message: message,
                        severity: NotificationSeverity.Warning,
                        source: NotificationSource.System,
                        correlationId: state.RuntimeId);
                }
                else
                {
                    state.RecentRemediationFailures++;
                    await notificationService.PublishAsync(
                        title: "Task runtime quarantine failed",
                        message: message,
                        severity: NotificationSeverity.Error,
                        source: NotificationSource.System,
                        correlationId: state.RuntimeId);
                }

                break;
            }
        }
    }

    private void UpdateOfflineRuntime(
        string runtimeId,
        string endpoint,
        int activeSlots,
        int maxSlots,
        TaskRuntimeRegistration? registration,
        DateTime now)
    {
        var state = _states.GetOrAdd(runtimeId, key => RuntimeHealthState.Create(key));
        var previous = state.Status;
        state.Endpoint = endpoint;
        state.ActiveSlots = activeSlots;
        state.MaxSlots = maxSlots;
        state.LastSeenUtc = now;
        state.IsRunning = false;
        state.IsOnline = registration?.Online ?? false;
        state.LastHeartbeatUtc = registration?.LastHeartbeatUtc;
        state.ConsecutiveProbeFailures = 0;
        state.Status = state.Quarantined ? TaskRuntimeHealthStatus.Quarantined : TaskRuntimeHealthStatus.Offline;
        state.LastReason = registration is null
            ? "runtime_not_registered"
            : registration.Online
                ? "runtime_not_running"
                : "runtime_offline";

        if (previous != state.Status && state.Status == TaskRuntimeHealthStatus.Offline)
        {
            AddIncident(runtimeId, state.Status, state.LastReason, "detect", false, $"Task runtime {runtimeId} is offline");
        }
    }

    private void PublishSnapshot(DateTime now, OrchestratorRuntimeSettings runtimeSettings)
    {
        var runtimes = _states.Values
            .OrderBy(x => x.RuntimeId, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.ToSnapshot())
            .ToList();

        var total = runtimes.Count;
        var healthy = runtimes.Count(x => x.Status == TaskRuntimeHealthStatus.Healthy);
        var degraded = runtimes.Count(x => x.Status == TaskRuntimeHealthStatus.Degraded);
        var unhealthy = runtimes.Count(x => x.Status == TaskRuntimeHealthStatus.Unhealthy);
        var recovering = runtimes.Count(x => x.Status == TaskRuntimeHealthStatus.Recovering);
        var offline = runtimes.Count(x => x.Status == TaskRuntimeHealthStatus.Offline);
        var quarantined = runtimes.Count(x => x.Status == TaskRuntimeHealthStatus.Quarantined);
        var readinessFailureCount = unhealthy + offline + quarantined;
        var readinessRatio = total == 0
            ? 0
            : (double)readinessFailureCount / total * 100.0;

        if (total > 0 && readinessRatio >= Math.Max(1, runtimeSettings.RuntimeReadinessFailureRatioPercent))
        {
            _readinessBlockedSinceUtc ??= now;
        }
        else
        {
            _readinessBlockedSinceUtc = null;
        }

        var readinessBlocked = _readinessBlockedSinceUtc.HasValue &&
                               now - _readinessBlockedSinceUtc.Value >=
                               TimeSpan.FromSeconds(Math.Max(10, runtimeSettings.RuntimeReadinessDegradeSeconds));

        var incidents = SnapshotIncidents();
        var remediationFailures = _states.Values.Sum(x => x.RecentRemediationFailures);
        var snapshot = new TaskRuntimeHealthSnapshot(
            GeneratedAtUtc: now,
            TotalRuntimes: total,
            HealthyRuntimes: healthy,
            DegradedRuntimes: degraded,
            UnhealthyRuntimes: unhealthy,
            RecoveringRuntimes: recovering,
            OfflineRuntimes: offline,
            QuarantinedRuntimes: quarantined,
            ReadinessBlocked: readinessBlocked,
            ReadinessBlockedSinceUtc: _readinessBlockedSinceUtc,
            LastRemediationAtUtc: _lastRemediationAtUtc,
            RecentRemediationFailures: remediationFailures,
            Runtimes: runtimes,
            Incidents: incidents);

        lock (_snapshotLock)
        {
            _snapshot = snapshot;
        }
    }

    private IReadOnlyList<TaskRuntimeHealthIncident> SnapshotIncidents()
    {
        lock (_incidentsLock)
        {
            return _incidents
                .Reverse()
                .Take(40)
                .ToList();
        }
    }

    private void AddIncident(
        string runtimeId,
        TaskRuntimeHealthStatus status,
        string reason,
        string action,
        bool success,
        string message)
    {
        var incident = new TaskRuntimeHealthIncident(
            Id: Guid.NewGuid().ToString("N"),
            TimestampUtc: DateTime.UtcNow,
            RuntimeId: runtimeId,
            Status: status,
            Reason: reason,
            Action: action,
            Success: success,
            Message: message);

        lock (_incidentsLock)
        {
            _incidents.AddLast(incident);
            while (_incidents.Count > MaxIncidents)
            {
                _incidents.RemoveFirst();
            }
        }
    }

    private void PruneState(DateTime now, IReadOnlySet<string> runningRuntimeIds, IEnumerable<string> knownRegistrationRuntimeIds)
    {
        var knownRuntimeIds = new HashSet<string>(runningRuntimeIds, StringComparer.OrdinalIgnoreCase);
        foreach (var runtimeId in knownRegistrationRuntimeIds)
        {
            knownRuntimeIds.Add(runtimeId);
        }

        foreach (var entry in _states.ToArray())
        {
            if (knownRuntimeIds.Contains(entry.Key))
            {
                continue;
            }

            if (now - entry.Value.LastSeenUtc < StateRetention)
            {
                continue;
            }

            _states.TryRemove(entry.Key, out _);
        }
    }

    private static string ResolveUnhealthyReason(
        DateTime now,
        TaskRuntimeRegistration? registration,
        bool heartbeatHealthy,
        bool probeHealthy,
        string probeError,
        TimeSpan heartbeatStaleAfter)
    {
        if (!heartbeatHealthy)
        {
            if (registration is null)
            {
                return "heartbeat_missing";
            }

            if (!registration.Online)
            {
                return "heartbeat_offline";
            }

            var ageSeconds = (int)Math.Max(0, (now - registration.LastHeartbeatUtc).TotalSeconds);
            return $"heartbeat_stale:{ageSeconds}s>{(int)heartbeatStaleAfter.TotalSeconds}s";
        }

        if (!probeHealthy)
        {
            if (string.IsNullOrWhiteSpace(probeError))
            {
                return "probe_failed";
            }

            return $"probe_failed:{probeError}";
        }

        return "unknown";
    }

    private sealed class RuntimeHealthState
    {
        public required string RuntimeId { get; init; }
        public TaskRuntimeHealthStatus Status { get; set; } = TaskRuntimeHealthStatus.Offline;
        public string LastReason { get; set; } = string.Empty;
        public int ConsecutiveProbeFailures { get; set; }
        public int RestartAttempts { get; set; }
        public DateTime? LastHeartbeatUtc { get; set; }
        public DateTime? LastProbeUtc { get; set; }
        public DateTime? LastHealthyUtc { get; set; }
        public DateTime? LastRemediationUtc { get; set; }
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
        public bool Quarantined { get; set; }
        public int RecentRemediationFailures { get; set; }
        public bool IsRunning { get; set; }
        public bool IsOnline { get; set; }
        public string Endpoint { get; set; } = string.Empty;
        public int ActiveSlots { get; set; }
        public int MaxSlots { get; set; }

        public static RuntimeHealthState Create(string runtimeId)
        {
            return new RuntimeHealthState
            {
                RuntimeId = runtimeId
            };
        }

        public TaskRuntimeHealthRuntimeSnapshot ToSnapshot()
        {
            return new TaskRuntimeHealthRuntimeSnapshot(
                RuntimeId: RuntimeId,
                Status: Status,
                Reason: LastReason,
                IsRunning: IsRunning,
                Online: IsOnline,
                LastHeartbeatUtc: LastHeartbeatUtc,
                LastProbeUtc: LastProbeUtc,
                LastHealthyUtc: LastHealthyUtc,
                LastRemediationUtc: LastRemediationUtc,
                ConsecutiveProbeFailures: ConsecutiveProbeFailures,
                RestartAttempts: RestartAttempts,
                Endpoint: Endpoint,
                ActiveSlots: ActiveSlots,
                MaxSlots: MaxSlots);
        }
    }
}

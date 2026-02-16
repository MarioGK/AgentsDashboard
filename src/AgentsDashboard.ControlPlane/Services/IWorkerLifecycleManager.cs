namespace AgentsDashboard.ControlPlane.Services;

public interface IWorkerLifecycleManager
{
    Task EnsureWorkerImageAvailableAsync(CancellationToken cancellationToken);
    Task EnsureMinimumWorkersAsync(CancellationToken cancellationToken);
    Task<WorkerLease?> AcquireWorkerForDispatchAsync(CancellationToken cancellationToken);
    Task<WorkerRuntime?> GetWorkerAsync(string workerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkerRuntime>> ListWorkersAsync(CancellationToken cancellationToken);
    Task ReportWorkerHeartbeatAsync(string workerId, int activeSlots, int maxSlots, CancellationToken cancellationToken);
    Task RecordDispatchActivityAsync(string workerId, CancellationToken cancellationToken);
    Task ScaleDownIdleWorkersAsync(CancellationToken cancellationToken);
    Task SetWorkerDrainingAsync(string workerId, bool draining, CancellationToken cancellationToken);
    Task RecycleWorkerAsync(string workerId, CancellationToken cancellationToken);
    Task RecycleWorkerPoolAsync(CancellationToken cancellationToken);
    Task RunReconciliationAsync(CancellationToken cancellationToken);
    Task SetScaleOutPausedAsync(bool paused, CancellationToken cancellationToken);
    Task ClearScaleOutCooldownAsync(CancellationToken cancellationToken);
    Task<OrchestratorHealthSnapshot> GetHealthSnapshotAsync(CancellationToken cancellationToken);
}

public sealed record WorkerLease(
    string WorkerId,
    string ContainerId,
    string GrpcEndpoint,
    string ProxyEndpoint);

public sealed record WorkerRuntime(
    string WorkerId,
    string ContainerId,
    string ContainerName,
    bool IsRunning,
    WorkerLifecycleState LifecycleState,
    bool IsDraining,
    string GrpcEndpoint,
    string ProxyEndpoint,
    int ActiveSlots,
    int MaxSlots,
    double CpuPercent,
    double MemoryPercent,
    DateTime LastActivityUtc,
    DateTime StartedAtUtc,
    int DispatchCount,
    string ImageRef,
    string ImageDigest,
    string ImageSource);

public enum WorkerLifecycleState
{
    Provisioning = 0,
    Starting = 1,
    Ready = 2,
    Busy = 3,
    Draining = 4,
    Stopping = 5,
    Stopped = 6,
    Quarantined = 7,
    FailedStart = 8
}

public sealed record OrchestratorHealthSnapshot(
    int RunningWorkers,
    int ReadyWorkers,
    int BusyWorkers,
    int DrainingWorkers,
    bool ScaleOutPaused,
    DateTime? ScaleOutCooldownUntilUtc,
    int StartAttemptsInWindow,
    int FailedStartsInWindow);

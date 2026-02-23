namespace AgentsDashboard.ControlPlane.Services;

public interface ITaskRuntimeLifecycleManager
{
    Task EnsureTaskRuntimeImageAvailableAsync(
        CancellationToken cancellationToken,
        IProgress<BackgroundWorkSnapshot>? progress = null);
    Task EnsureMinimumTaskRuntimesAsync(CancellationToken cancellationToken);
    Task<TaskRuntimeLease?> AcquireTaskRuntimeForDispatchAsync(
        string repositoryId,
        string taskId,
        int requestedParallelSlots,
        CancellationToken cancellationToken);
    Task<TaskRuntimeInstance?> GetTaskRuntimeAsync(string runtimeId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TaskRuntimeInstance>> ListTaskRuntimesAsync(CancellationToken cancellationToken);
    Task ReportTaskRuntimeHeartbeatAsync(string runtimeId, int activeSlots, int maxSlots, CancellationToken cancellationToken);
    Task RecordDispatchActivityAsync(string runtimeId, CancellationToken cancellationToken);
    Task ScaleDownIdleTaskRuntimesAsync(CancellationToken cancellationToken);
    Task SetTaskRuntimeDrainingAsync(string runtimeId, bool draining, CancellationToken cancellationToken);
    Task<bool> RestartTaskRuntimeAsync(string runtimeId, CancellationToken cancellationToken);
    Task RecycleTaskRuntimeAsync(string runtimeId, CancellationToken cancellationToken);
    Task RecycleTaskRuntimePoolAsync(CancellationToken cancellationToken);
    Task RunReconciliationAsync(CancellationToken cancellationToken);
    Task SetScaleOutPausedAsync(bool paused, CancellationToken cancellationToken);
    Task ClearScaleOutCooldownAsync(CancellationToken cancellationToken);
    Task<OrchestratorHealthSnapshot> GetHealthSnapshotAsync(CancellationToken cancellationToken);
}



public enum TaskRuntimeLifecycleState
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

public sealed record TaskRuntimeLease(
    string TaskRuntimeId,
    string ContainerId,
    string GrpcEndpoint);

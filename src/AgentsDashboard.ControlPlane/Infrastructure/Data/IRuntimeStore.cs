namespace AgentsDashboard.ControlPlane.Infrastructure.Data;

public interface IRuntimeStore
{
    Task<List<TaskRuntimeRegistration>> ListTaskRuntimeRegistrationsAsync(CancellationToken cancellationToken);
    Task UpsertTaskRuntimeRegistrationHeartbeatAsync(string runtimeId, string endpoint, int activeSlots, int maxSlots, CancellationToken cancellationToken);
    Task MarkStaleTaskRuntimeRegistrationsOfflineAsync(TimeSpan threshold, CancellationToken cancellationToken);
    Task<List<TaskRuntimeDocument>> ListTaskRuntimesAsync(CancellationToken cancellationToken);
    Task<TaskRuntimeDocument> UpsertTaskRuntimeStateAsync(TaskRuntimeStateUpdate update, CancellationToken cancellationToken);
    Task<TaskRuntimeTelemetrySnapshot> GetTaskRuntimeTelemetryAsync(CancellationToken cancellationToken);

    Task<bool> TryAcquireLeaseAsync(string leaseName, string ownerId, TimeSpan ttl, CancellationToken cancellationToken);
    Task<bool> RenewLeaseAsync(string leaseName, string ownerId, TimeSpan ttl, CancellationToken cancellationToken);
    Task ReleaseLeaseAsync(string leaseName, string ownerId, CancellationToken cancellationToken);
}

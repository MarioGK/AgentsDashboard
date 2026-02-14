namespace AgentsDashboard.ControlPlane.Services;

public interface IWorkerLifecycleManager
{
    Task<bool> EnsureWorkerRunningAsync(CancellationToken cancellationToken);
    Task RecordDispatchActivityAsync(CancellationToken cancellationToken);
    Task StopWorkerIfIdleAsync(CancellationToken cancellationToken);
}

using MagicOnion;
using MessagePack;

namespace AgentsDashboard.Contracts.Worker;

/// <summary>
/// Receiver interface for broadcast commands FROM control plane TO workers.
/// Workers implement this to receive broadcast commands.
/// </summary>
public interface IWorkerControlReceiver
{
    /// <summary>
    /// Called when control plane requests status from all workers.
    /// </summary>
    void OnStatusRequest(StatusRequestMessage request);

    /// <summary>
    /// Called when configuration has changed and workers should refresh.
    /// </summary>
    void OnConfigurationChanged(ConfigurationChangedMessage message);

    /// <summary>
    /// Called when control plane requests workers to shut down.
    /// </summary>
    void OnShutdownRequest(ShutdownRequestMessage request);
}

/// <summary>
/// StreamingHub interface for worker control via multicaster.
/// Workers register themselves; control plane broadcasts to all registered workers.
/// </summary>
public interface IWorkerControlHub : IStreamingHub<IWorkerControlHub, IWorkerControlReceiver>
{
    /// <summary>
    /// Register this worker with the control plane.
    /// </summary>
    Task<WorkerRegistrationResult> RegisterAsync(WorkerRegistrationRequest request);

    /// <summary>
    /// Unregister this worker from the control plane.
    /// </summary>
    Task UnregisterAsync();

    /// <summary>
    /// Report worker status to control plane.
    /// </summary>
    Task ReportStatusAsync(WorkerStatusReport report);
}

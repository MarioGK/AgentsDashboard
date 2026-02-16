using MagicOnion;
using MessagePack;

namespace AgentsDashboard.Contracts.Worker;

/// <summary>
/// Receiver interface for events pushed FROM worker TO control plane.
/// The control plane implements this to receive callbacks.
/// </summary>
public interface IWorkerEventReceiver
{
    /// <summary>
    /// Called when a job event occurs (started, completed, failed, log, etc.).
    /// </summary>
    void OnJobEvent(JobEventMessage eventMessage);

    /// <summary>
    /// Called when worker status changes.
    /// </summary>
    void OnWorkerStatusChanged(WorkerStatusMessage statusMessage);
}

/// <summary>
/// StreamingHub interface for bidirectional real-time communication.
/// Workers connect and push events; control plane subscribes to specific runs.
/// Replaces the server-streaming SubscribeEvents gRPC method.
/// </summary>
public interface IWorkerEventHub : IStreamingHub<IWorkerEventHub, IWorkerEventReceiver>
{
    /// <summary>
    /// Subscribe to events. Optionally filter by specific run IDs.
    /// </summary>
    Task SubscribeAsync(string[]? runIds = null);

    /// <summary>
    /// Unsubscribe from all events.
    /// </summary>
    Task UnsubscribeAsync();
}

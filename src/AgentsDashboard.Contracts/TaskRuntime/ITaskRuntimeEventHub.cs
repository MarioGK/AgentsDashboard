using MagicOnion;
using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

/// <summary>
/// Receiver interface for events pushed FROM worker TO control plane.
/// The control plane implements this to receive callbacks.
/// </summary>
public interface ITaskRuntimeEventReceiver
{
    /// <summary>
    /// Called when a job event occurs (started, completed, failed, log, etc.).
    /// </summary>
    void OnJobEvent(JobEventMessage eventMessage);

    /// <summary>
    /// Called when task runtime status changes.
    /// </summary>
    void OnTaskRuntimeStatusChanged(TaskRuntimeStatusMessage statusMessage);
}

/// <summary>
/// StreamingHub interface for bidirectional real-time communication.
/// Workers connect and push events; control plane subscribes to specific runs.
/// Replaces the server-streaming SubscribeEvents gRPC method.
/// </summary>
public interface ITaskRuntimeEventHub : IStreamingHub<ITaskRuntimeEventHub, ITaskRuntimeEventReceiver>
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

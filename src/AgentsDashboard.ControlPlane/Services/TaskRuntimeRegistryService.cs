using AgentsDashboard.Contracts.TaskRuntime;

namespace AgentsDashboard.ControlPlane.Services;

public interface ITaskRuntimeRegistryService
{
    /// <summary>
    /// Record a task runtime heartbeat (called by TaskRuntimeEventListenerService when receiving heartbeats).
    /// </summary>
    void RecordHeartbeat(string runtimeId, string hostName, int activeSlots, int maxSlots);

    /// <summary>
    /// Broadcast a status request to all registered task runtimes via TaskRuntimeGateway.
    /// </summary>
    Task BroadcastStatusRequestAsync(StatusRequestMessage request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the count of registered task runtimes.
    /// </summary>
    int GetRegisteredTaskRuntimeCount();

    /// <summary>
    /// Get information about registered task runtimes.
    /// </summary>
    IEnumerable<TaskRuntimeInfo> GetRegisteredTaskRuntimes();
}


public record TaskRuntimeInfo(string TaskRuntimeId, string HostName, int ActiveSlots, int MaxSlots, DateTimeOffset LastHeartbeat);

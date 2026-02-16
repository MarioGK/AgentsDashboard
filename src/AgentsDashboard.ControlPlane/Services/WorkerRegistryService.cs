using AgentsDashboard.Contracts.Worker;

namespace AgentsDashboard.ControlPlane.Services;

public interface IWorkerRegistryService
{
    /// <summary>
    /// Record a worker heartbeat (called by WorkerEventListenerService when receiving heartbeats).
    /// </summary>
    void RecordHeartbeat(string workerId, string hostName, int activeSlots, int maxSlots);

    /// <summary>
    /// Broadcast a status request to all registered workers via WorkerGateway.
    /// </summary>
    Task BroadcastStatusRequestAsync(StatusRequestMessage request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the count of registered workers.
    /// </summary>
    int GetRegisteredWorkerCount();

    /// <summary>
    /// Get information about registered workers.
    /// </summary>
    IEnumerable<WorkerInfo> GetRegisteredWorkers();
}

public record WorkerInfo(string WorkerId, string HostName, int ActiveSlots, int MaxSlots, DateTimeOffset LastHeartbeat);

public class WorkerRegistryService : IWorkerRegistryService
{
    private readonly IMagicOnionClientFactory _clientFactory;
    private readonly Dictionary<string, WorkerInfo> _registeredWorkers = new();
    private readonly object _lock = new();

    public WorkerRegistryService(IMagicOnionClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public void RecordHeartbeat(string workerId, string hostName, int activeSlots, int maxSlots)
    {
        lock (_lock)
        {
            _registeredWorkers[workerId] = new WorkerInfo(
                workerId,
                hostName,
                activeSlots,
                maxSlots,
                DateTimeOffset.UtcNow);
        }
    }

    public async Task BroadcastStatusRequestAsync(StatusRequestMessage request, CancellationToken cancellationToken = default)
    {
        // Send heartbeat request to WorkerGateway which will broadcast to all connected workers
        var client = _clientFactory.CreateWorkerGatewayService();
        var heartbeatRequest = new HeartbeatRequest
        {
            WorkerId = "control-plane-status-request",
            HostName = "control-plane",
            ActiveSlots = 0,
            MaxSlots = 0,
            Timestamp = DateTimeOffset.UtcNow
        };

        // The heartbeat triggers workers to report their status
        await client.HeartbeatAsync(heartbeatRequest);
    }

    public int GetRegisteredWorkerCount()
    {
        lock (_lock)
        {
            return _registeredWorkers.Count;
        }
    }

    public IEnumerable<WorkerInfo> GetRegisteredWorkers()
    {
        lock (_lock)
        {
            return _registeredWorkers.Values.ToList();
        }
    }
}

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
    private readonly ILogger<WorkerRegistryService> _logger;
    private readonly Dictionary<string, WorkerInfo> _registeredWorkers = new();
    private readonly object _lock = new();
    private static readonly TimeSpan WorkerTtl = TimeSpan.FromMinutes(2);

    public WorkerRegistryService(IMagicOnionClientFactory clientFactory, ILogger<WorkerRegistryService> logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;
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
        cancellationToken.ThrowIfCancellationRequested();

        PruneExpiredWorkers();

        // WorkerGateway currently exposes status poke via HeartbeatAsync.
        // Keep this as the control-plane trigger until control-hub broadcast is wired end-to-end.
        var client = _clientFactory.CreateWorkerGatewayService();
        var heartbeatRequest = new HeartbeatRequest
        {
            WorkerId = $"control-plane-status-request-{request.RequestId}",
            HostName = "control-plane",
            ActiveSlots = 0,
            MaxSlots = 0,
            Timestamp = DateTimeOffset.UtcNow
        };

        await client.HeartbeatAsync(heartbeatRequest);
        _logger.LogInformation("Issued worker status request {RequestId}", request.RequestId);
    }

    public int GetRegisteredWorkerCount()
    {
        lock (_lock)
        {
            RemoveExpiredWorkersUnsafe(DateTimeOffset.UtcNow);
            return _registeredWorkers.Count;
        }
    }

    public IEnumerable<WorkerInfo> GetRegisteredWorkers()
    {
        lock (_lock)
        {
            RemoveExpiredWorkersUnsafe(DateTimeOffset.UtcNow);
            return _registeredWorkers.Values.ToList();
        }
    }

    private void PruneExpiredWorkers()
    {
        lock (_lock)
        {
            RemoveExpiredWorkersUnsafe(DateTimeOffset.UtcNow);
        }
    }

    private void RemoveExpiredWorkersUnsafe(DateTimeOffset now)
    {
        var expiredWorkerIds = _registeredWorkers
            .Where(pair => now - pair.Value.LastHeartbeat > WorkerTtl)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var workerId in expiredWorkerIds)
        {
            _registeredWorkers.Remove(workerId);
        }
    }
}

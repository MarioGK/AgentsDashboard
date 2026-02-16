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
    private readonly IWorkerLifecycleManager _lifecycleManager;
    private readonly ILogger<WorkerRegistryService> _logger;
    private readonly Dictionary<string, WorkerInfo> _registeredWorkers = new();
    private readonly object _lock = new();
    private static readonly TimeSpan WorkerTtl = TimeSpan.FromMinutes(2);

    public WorkerRegistryService(
        IMagicOnionClientFactory clientFactory,
        IWorkerLifecycleManager lifecycleManager,
        ILogger<WorkerRegistryService> logger)
    {
        _clientFactory = clientFactory;
        _lifecycleManager = lifecycleManager;
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

        var workers = await _lifecycleManager.ListWorkersAsync(cancellationToken);
        if (workers.Count == 0)
        {
            _logger.LogInformation("No workers available for status request {RequestId}", request.RequestId);
            return;
        }

        foreach (var worker in workers.Where(x => x.IsRunning))
        {
            try
            {
                var client = _clientFactory.CreateWorkerGatewayService(worker.WorkerId, worker.GrpcEndpoint);
                await client.HeartbeatAsync(new HeartbeatRequest
                {
                    WorkerId = $"control-plane-status-request-{request.RequestId}",
                    HostName = "control-plane",
                    ActiveSlots = 0,
                    MaxSlots = 0,
                    Timestamp = DateTimeOffset.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Status request heartbeat failed for worker {WorkerId}", worker.WorkerId);
            }
        }

        _logger.LogInformation("Issued worker status request {RequestId} to {Count} workers", request.RequestId, workers.Count(x => x.IsRunning));
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

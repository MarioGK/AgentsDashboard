using AgentsDashboard.Contracts.TaskRuntime;

namespace AgentsDashboard.ControlPlane.Services;

public interface ITaskRuntimeRegistryService
{
    /// <summary>
    /// Record a worker heartbeat (called by TaskRuntimeEventListenerService when receiving heartbeats).
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
    IEnumerable<TaskRuntimeInfo> GetRegisteredWorkers();
}

public record TaskRuntimeInfo(string TaskRuntimeId, string HostName, int ActiveSlots, int MaxSlots, DateTimeOffset LastHeartbeat);

public class TaskRuntimeRegistryService : ITaskRuntimeRegistryService
{
    private readonly IMagicOnionClientFactory _clientFactory;
    private readonly ITaskRuntimeLifecycleManager _lifecycleManager;
    private readonly ILogger<TaskRuntimeRegistryService> _logger;
    private readonly Dictionary<string, TaskRuntimeInfo> _registeredWorkers = new();
    private readonly object _lock = new();
    private static readonly TimeSpan WorkerTtl = TimeSpan.FromMinutes(2);

    public TaskRuntimeRegistryService(
        IMagicOnionClientFactory clientFactory,
        ITaskRuntimeLifecycleManager lifecycleManager,
        ILogger<TaskRuntimeRegistryService> logger)
    {
        _clientFactory = clientFactory;
        _lifecycleManager = lifecycleManager;
        _logger = logger;
    }

    public void RecordHeartbeat(string workerId, string hostName, int activeSlots, int maxSlots)
    {
        lock (_lock)
        {
            _registeredWorkers[workerId] = new TaskRuntimeInfo(
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

        var workers = await _lifecycleManager.ListTaskRuntimesAsync(cancellationToken);
        if (workers.Count == 0)
        {
            _logger.ZLogInformation("No workers available for status request {RequestId}", request.RequestId);
            return;
        }

        foreach (var worker in workers.Where(x => x.IsRunning))
        {
            try
            {
                var client = _clientFactory.CreateTaskRuntimeGatewayService(worker.TaskRuntimeId, worker.GrpcEndpoint);
                await client.HeartbeatAsync(new HeartbeatRequest
                {
                    TaskRuntimeId = $"control-plane-status-request-{request.RequestId}",
                    HostName = "control-plane",
                    ActiveSlots = 0,
                    MaxSlots = 0,
                    Timestamp = DateTimeOffset.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.ZLogDebug(ex, "Status request heartbeat failed for worker {TaskRuntimeId}", worker.TaskRuntimeId);
            }
        }

        _logger.ZLogInformation("Issued worker status request {RequestId} to {Count} workers", request.RequestId, workers.Count(x => x.IsRunning));
    }

    public int GetRegisteredWorkerCount()
    {
        lock (_lock)
        {
            RemoveExpiredWorkersUnsafe(DateTimeOffset.UtcNow);
            return _registeredWorkers.Count;
        }
    }

    public IEnumerable<TaskRuntimeInfo> GetRegisteredWorkers()
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
        var expiredTaskRuntimeIds = _registeredWorkers
            .Where(pair => now - pair.Value.LastHeartbeat > WorkerTtl)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var workerId in expiredTaskRuntimeIds)
        {
            _registeredWorkers.Remove(workerId);
        }
    }
}

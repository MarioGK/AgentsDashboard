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

public class TaskRuntimeRegistryService : ITaskRuntimeRegistryService
{
    private readonly IMagicOnionClientFactory _clientFactory;
    private readonly ITaskRuntimeLifecycleManager _lifecycleManager;
    private readonly ILogger<TaskRuntimeRegistryService> _logger;
    private readonly Dictionary<string, TaskRuntimeInfo> _registeredTaskRuntimes = new();
    private readonly object _lock = new();
    private static readonly TimeSpan TaskRuntimeTtl = TimeSpan.FromMinutes(2);

    public TaskRuntimeRegistryService(
        IMagicOnionClientFactory clientFactory,
        ITaskRuntimeLifecycleManager lifecycleManager,
        ILogger<TaskRuntimeRegistryService> logger)
    {
        _clientFactory = clientFactory;
        _lifecycleManager = lifecycleManager;
        _logger = logger;
    }

    public void RecordHeartbeat(string runtimeId, string hostName, int activeSlots, int maxSlots)
    {
        lock (_lock)
        {
            _registeredTaskRuntimes[runtimeId] = new TaskRuntimeInfo(
                runtimeId,
                hostName,
                activeSlots,
                maxSlots,
                DateTimeOffset.UtcNow);
        }
    }

    public async Task BroadcastStatusRequestAsync(StatusRequestMessage request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        PruneExpiredTaskRuntimes();

        var taskRuntimes = await _lifecycleManager.ListTaskRuntimesAsync(cancellationToken);
        if (taskRuntimes.Count == 0)
        {
            _logger.ZLogInformation("No task runtimes available for status request {RequestId}", request.RequestId);
            return;
        }

        foreach (var runtime in taskRuntimes.Where(x => x.IsRunning))
        {
            try
            {
                var client = _clientFactory.CreateTaskRuntimeGatewayService(runtime.TaskRuntimeId, runtime.GrpcEndpoint);
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
                _logger.ZLogDebug(ex, "Status request heartbeat failed for task runtime {TaskRuntimeId}", runtime.TaskRuntimeId);
            }
        }

        _logger.ZLogInformation("Issued task runtime status request {RequestId} to {Count} task runtimes", request.RequestId, taskRuntimes.Count(x => x.IsRunning));
    }

    public int GetRegisteredTaskRuntimeCount()
    {
        lock (_lock)
        {
            RemoveExpiredTaskRuntimesUnsafe(DateTimeOffset.UtcNow);
            return _registeredTaskRuntimes.Count;
        }
    }

    public IEnumerable<TaskRuntimeInfo> GetRegisteredTaskRuntimes()
    {
        lock (_lock)
        {
            RemoveExpiredTaskRuntimesUnsafe(DateTimeOffset.UtcNow);
            return _registeredTaskRuntimes.Values.ToList();
        }
    }

    private void PruneExpiredTaskRuntimes()
    {
        lock (_lock)
        {
            RemoveExpiredTaskRuntimesUnsafe(DateTimeOffset.UtcNow);
        }
    }

    private void RemoveExpiredTaskRuntimesUnsafe(DateTimeOffset now)
    {
        var expiredTaskRuntimeIds = _registeredTaskRuntimes
            .Where(pair => now - pair.Value.LastHeartbeat > TaskRuntimeTtl)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var runtimeId in expiredTaskRuntimeIds)
        {
            _registeredTaskRuntimes.Remove(runtimeId);
        }
    }
}

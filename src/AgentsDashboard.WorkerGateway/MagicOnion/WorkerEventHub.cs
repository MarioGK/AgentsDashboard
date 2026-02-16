using MagicOnion.Server.Hubs;
using AgentsDashboard.Contracts.Worker;
using Microsoft.Extensions.Logging;

namespace AgentsDashboard.WorkerGateway.MagicOnion;

/// <summary>
/// StreamingHub for broadcasting job events from worker to control plane.
/// The control plane connects as a client and subscribes to specific run IDs.
/// </summary>
public sealed class WorkerEventHub : StreamingHubBase<IWorkerEventHub, IWorkerEventReceiver>, IWorkerEventHub
{
    private readonly ILogger<WorkerEventHub> _logger;
    private IWorkerEventReceiver? _receiver;
    private IGroup<IWorkerEventReceiver>? _allGroup;
    private readonly List<string> _subscribedRunIds = [];

    /// <summary>
    /// Static broadcaster for sending events from outside the hub.
    /// </summary>
    private static readonly WorkerEventBroadcaster Broadcaster = new();

    public WorkerEventHub(ILogger<WorkerEventHub> logger)
    {
        _logger = logger;
    }

    protected override ValueTask OnConnecting()
    {
        _receiver = Receiver;
        _logger.LogDebug("Client connecting to event hub");
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDisconnected()
    {
        _logger.LogDebug("Client disconnecting from event hub");

        // Clear subscriptions - the connection is already closing
        _subscribedRunIds.Clear();
        _allGroup = null;

        return ValueTask.CompletedTask;
    }

    public async Task SubscribeAsync(string[]? runIds = null)
    {
        if (runIds == null || runIds.Length == 0)
        {
            _allGroup = await Group.AddAsync("all");
            _subscribedRunIds.Clear();
            _logger.LogDebug("Client subscribed to all events");
        }
        else
        {
            foreach (var runId in runIds)
            {
                if (string.IsNullOrWhiteSpace(runId)) continue;

                await Group.AddAsync(runId);
                _subscribedRunIds.Add(runId);
                _logger.LogDebug("Client subscribed to run {RunId}", runId);
            }
        }
    }

    public async Task UnsubscribeAsync()
    {
        // Clear local tracking - groups are per-connection so just clear the list
        _subscribedRunIds.Clear();
        _allGroup = null;

        _logger.LogDebug("Client unsubscribed from all events");

        await Task.CompletedTask;
    }

    /// <summary>
    /// Broadcasts a job event to subscribers of the specified run ID.
    /// </summary>
    public static async Task BroadcastJobEventAsync(JobEventMessage eventMessage)
    {
        await Broadcaster.BroadcastJobEventAsync(eventMessage);
    }

    /// <summary>
    /// Broadcasts a worker status message to all subscribers.
    /// </summary>
    public static async Task BroadcastWorkerStatusAsync(WorkerStatusMessage statusMessage)
    {
        await Broadcaster.BroadcastWorkerStatusAsync(statusMessage);
    }
}

/// <summary>
/// Internal broadcaster for worker events.
/// Uses a simple in-memory approach to track groups for broadcasting.
/// </summary>
internal sealed class WorkerEventBroadcaster
{
    private readonly object _lock = new();
    private readonly List<IWorkerEventReceiver> _allReceivers = [];
    private readonly Dictionary<string, List<IWorkerEventReceiver>> _runReceivers = new();

    public void RegisterReceiver(string? runId, IWorkerEventReceiver receiver)
    {
        lock (_lock)
        {
            _allReceivers.Add(receiver);
            if (!string.IsNullOrEmpty(runId))
            {
                if (!_runReceivers.TryGetValue(runId, out var list))
                {
                    list = [];
                    _runReceivers[runId] = list;
                }
                list.Add(receiver);
            }
        }
    }

    public void UnregisterReceiver(IWorkerEventReceiver receiver)
    {
        lock (_lock)
        {
            _allReceivers.Remove(receiver);
            foreach (var list in _runReceivers.Values)
            {
                list.Remove(receiver);
            }
        }
    }

    public async Task BroadcastJobEventAsync(JobEventMessage eventMessage)
    {
        List<IWorkerEventReceiver>? receiversCopy;
        lock (_lock)
        {
            receiversCopy = _allReceivers.ToList();
        }

        foreach (var receiver in receiversCopy)
        {
            try
            {
                receiver.OnJobEvent(eventMessage);
            }
            catch
            {
                // Ignore errors when broadcasting
            }
        }

        await Task.CompletedTask;
    }

    public async Task BroadcastWorkerStatusAsync(WorkerStatusMessage statusMessage)
    {
        List<IWorkerEventReceiver>? receiversCopy;
        lock (_lock)
        {
            receiversCopy = _allReceivers.ToList();
        }

        foreach (var receiver in receiversCopy)
        {
            try
            {
                receiver.OnWorkerStatusChanged(statusMessage);
            }
            catch
            {
                // Ignore errors when broadcasting
            }
        }

        await Task.CompletedTask;
    }
}

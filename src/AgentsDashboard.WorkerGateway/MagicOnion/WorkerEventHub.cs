using AgentsDashboard.Contracts.Worker;
using Cysharp.Runtime.Multicast;
using MagicOnion.Server.Hubs;
using Microsoft.Extensions.Logging;

namespace AgentsDashboard.WorkerGateway.MagicOnion;

/// <summary>
/// StreamingHub for broadcasting job events from worker to control plane.
/// The control plane connects as a client and subscribes to specific run IDs.
/// </summary>
public sealed class WorkerEventHub : StreamingHubBase<IWorkerEventHub, IWorkerEventReceiver>, IWorkerEventHub
{
    private const string AllEventsGroupName = "worker-events:all";
    private readonly ILogger<WorkerEventHub> _logger;
    private readonly HashSet<string> _subscribedRunIds = [];
    private Guid _connectionId;
    private bool _subscribedToAll;
    private static readonly object ProviderLock = new();
    private static IMulticastGroupProvider? _groupProvider;

    public WorkerEventHub(ILogger<WorkerEventHub> logger, IMulticastGroupProvider groupProvider)
    {
        _logger = logger;
        lock (ProviderLock)
        {
            _groupProvider ??= groupProvider;
        }
    }

    protected override ValueTask OnConnecting()
    {
        _connectionId = ConnectionId;
        _logger.LogDebug("Client connecting to event hub");
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDisconnected()
    {
        _logger.LogDebug("Client disconnecting from event hub");

        RemoveCurrentConnectionFromGroups();

        return ValueTask.CompletedTask;
    }

    public Task SubscribeAsync(string[]? runIds = null)
    {
        RemoveCurrentConnectionFromGroups();

        if (runIds == null || runIds.Length == 0)
        {
            GetAllGroup().Add(_connectionId, Client);
            _subscribedToAll = true;
            _logger.LogDebug("Client subscribed to all events");
        }
        else
        {
            foreach (var runId in runIds)
            {
                if (string.IsNullOrWhiteSpace(runId))
                    continue;

                GetRunGroup(runId).Add(_connectionId, Client);
                _subscribedRunIds.Add(runId);
                _logger.LogDebug("Client subscribed to run {RunId}", runId);
            }
        }

        return Task.CompletedTask;
    }

    public Task UnsubscribeAsync()
    {
        RemoveCurrentConnectionFromGroups();

        _logger.LogDebug("Client unsubscribed from all events");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Broadcasts a job event to subscribers of the specified run ID.
    /// </summary>
    public static async Task BroadcastJobEventAsync(JobEventMessage eventMessage)
    {
        var groupProvider = _groupProvider;
        if (groupProvider is null)
        {
            return;
        }

        groupProvider.GetOrAddSynchronousGroup<Guid, IWorkerEventReceiver>(AllEventsGroupName)
            .All
            .OnJobEvent(eventMessage);

        if (!string.IsNullOrWhiteSpace(eventMessage.RunId))
        {
            groupProvider.GetOrAddSynchronousGroup<Guid, IWorkerEventReceiver>(GetRunGroupName(eventMessage.RunId))
                .All
                .OnJobEvent(eventMessage);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Broadcasts a worker status message to all subscribers.
    /// </summary>
    public static async Task BroadcastWorkerStatusAsync(WorkerStatusMessage statusMessage)
    {
        var groupProvider = _groupProvider;
        if (groupProvider is null)
        {
            return;
        }

        groupProvider.GetOrAddSynchronousGroup<Guid, IWorkerEventReceiver>(AllEventsGroupName)
            .All
            .OnWorkerStatusChanged(statusMessage);

        await Task.CompletedTask;
    }

    private void RemoveCurrentConnectionFromGroups()
    {
        var groupProvider = _groupProvider;
        if (groupProvider is null)
        {
            return;
        }

        if (_subscribedToAll)
        {
            groupProvider.GetOrAddSynchronousGroup<Guid, IWorkerEventReceiver>(AllEventsGroupName)
                .Remove(_connectionId);
        }

        foreach (var runId in _subscribedRunIds)
        {
            groupProvider.GetOrAddSynchronousGroup<Guid, IWorkerEventReceiver>(GetRunGroupName(runId))
                .Remove(_connectionId);
        }

        _subscribedToAll = false;
        _subscribedRunIds.Clear();
    }

    private static IMulticastSyncGroup<Guid, IWorkerEventReceiver> GetAllGroup()
    {
        return _groupProvider!
            .GetOrAddSynchronousGroup<Guid, IWorkerEventReceiver>(AllEventsGroupName);
    }

    private static IMulticastSyncGroup<Guid, IWorkerEventReceiver> GetRunGroup(string runId)
    {
        return _groupProvider!
            .GetOrAddSynchronousGroup<Guid, IWorkerEventReceiver>(GetRunGroupName(runId));
    }

    private static string GetRunGroupName(string runId)
    {
        return $"worker-events:run:{runId}";
    }
}

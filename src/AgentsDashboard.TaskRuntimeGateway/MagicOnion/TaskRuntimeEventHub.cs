using AgentsDashboard.Contracts.TaskRuntime;
using Cysharp.Runtime.Multicast;
using MagicOnion.Server.Hubs;
using Microsoft.Extensions.Logging;

namespace AgentsDashboard.TaskRuntimeGateway.MagicOnion;

/// <summary>
/// StreamingHub for broadcasting job events from worker to control plane.
/// The control plane connects as a client and subscribes to specific run IDs.
/// </summary>
public sealed class TaskRuntimeEventHub : StreamingHubBase<ITaskRuntimeEventHub, ITaskRuntimeEventReceiver>, ITaskRuntimeEventHub
{
    private const string AllEventsGroupName = "worker-events:all";
    private readonly ILogger<TaskRuntimeEventHub> _logger;
    private readonly HashSet<string> _subscribedRunIds = [];
    private Guid _connectionId;
    private bool _subscribedToAll;
    private static readonly object ProviderLock = new();
    private static IMulticastGroupProvider? _groupProvider;

    public TaskRuntimeEventHub(ILogger<TaskRuntimeEventHub> logger, IMulticastGroupProvider groupProvider)
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
        _logger.ZLogDebug("Client connecting to event hub");
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDisconnected()
    {
        _logger.ZLogDebug("Client disconnecting from event hub");

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
            _logger.ZLogDebug("Client subscribed to all events");
        }
        else
        {
            foreach (var runId in runIds)
            {
                if (string.IsNullOrWhiteSpace(runId))
                    continue;

                GetRunGroup(runId).Add(_connectionId, Client);
                _subscribedRunIds.Add(runId);
                _logger.ZLogDebug("Client subscribed to run {RunId}", runId);
            }
        }

        return Task.CompletedTask;
    }

    public Task UnsubscribeAsync()
    {
        RemoveCurrentConnectionFromGroups();

        _logger.ZLogDebug("Client unsubscribed from all events");

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

        groupProvider.GetOrAddSynchronousGroup<Guid, ITaskRuntimeEventReceiver>(AllEventsGroupName)
            .All
            .OnJobEvent(eventMessage);

        if (!string.IsNullOrWhiteSpace(eventMessage.RunId))
        {
            groupProvider.GetOrAddSynchronousGroup<Guid, ITaskRuntimeEventReceiver>(GetRunGroupName(eventMessage.RunId))
                .All
                .OnJobEvent(eventMessage);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Broadcasts a task runtime status message to all subscribers.
    /// </summary>
    public static async Task BroadcastTaskRuntimeStatusAsync(TaskRuntimeStatusMessage statusMessage)
    {
        var groupProvider = _groupProvider;
        if (groupProvider is null)
        {
            return;
        }

        groupProvider.GetOrAddSynchronousGroup<Guid, ITaskRuntimeEventReceiver>(AllEventsGroupName)
            .All
            .OnTaskRuntimeStatusChanged(statusMessage);

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
            groupProvider.GetOrAddSynchronousGroup<Guid, ITaskRuntimeEventReceiver>(AllEventsGroupName)
                .Remove(_connectionId);
        }

        foreach (var runId in _subscribedRunIds)
        {
            groupProvider.GetOrAddSynchronousGroup<Guid, ITaskRuntimeEventReceiver>(GetRunGroupName(runId))
                .Remove(_connectionId);
        }

        _subscribedToAll = false;
        _subscribedRunIds.Clear();
    }

    private static IMulticastSyncGroup<Guid, ITaskRuntimeEventReceiver> GetAllGroup()
    {
        return _groupProvider!
            .GetOrAddSynchronousGroup<Guid, ITaskRuntimeEventReceiver>(AllEventsGroupName);
    }

    private static IMulticastSyncGroup<Guid, ITaskRuntimeEventReceiver> GetRunGroup(string runId)
    {
        return _groupProvider!
            .GetOrAddSynchronousGroup<Guid, ITaskRuntimeEventReceiver>(GetRunGroupName(runId));
    }

    private static string GetRunGroupName(string runId)
    {
        return $"worker-events:run:{runId}";
    }
}

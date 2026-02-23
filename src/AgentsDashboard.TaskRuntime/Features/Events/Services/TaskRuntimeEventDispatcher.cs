using System.Collections.Concurrent;
using System.Collections.Generic;


namespace AgentsDashboard.TaskRuntime.Features.Events.Services;

public sealed class TaskRuntimeEventDispatcher(
    ILogger<TaskRuntimeEventDispatcher> logger)
{
    private readonly ConcurrentDictionary<Guid, TaskRuntimeEventSubscription> _subscriptions = new();

    public void RegisterConnection(Guid connectionId, ITaskRuntimeEventReceiver receiver)
    {
        _subscriptions[connectionId] = new TaskRuntimeEventSubscription(receiver);
    }

    public void UnregisterConnection(Guid connectionId)
    {
        _subscriptions.TryRemove(connectionId, out _);
    }

    public void SubscribeAll(Guid connectionId)
    {
        if (!_subscriptions.TryGetValue(connectionId, out var subscription))
        {
            return;
        }

        subscription.SubscribeAll();
    }

    public void SubscribeRunIds(Guid connectionId, string[] runIds)
    {
        if (!_subscriptions.TryGetValue(connectionId, out var subscription))
        {
            return;
        }

        subscription.SubscribeRunIds(runIds);
    }

    public void Unsubscribe(Guid connectionId)
    {
        if (!_subscriptions.TryGetValue(connectionId, out var subscription))
        {
            return;
        }

        subscription.Unsubscribe();
    }

    public Task BroadcastJobEventAsync(JobEventMessage eventMessage)
    {
        foreach (var subscription in _subscriptions.Values)
        {
            if (!subscription.ShouldReceiveJobEvent(eventMessage.RunId))
            {
                continue;
            }

            try
            {
                subscription.Receiver.OnJobEvent(eventMessage);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to dispatch job event to receiver");
            }
        }

        return Task.CompletedTask;
    }

    public Task BroadcastTaskRuntimeStatusAsync(TaskRuntimeStatusMessage statusMessage)
    {
        foreach (var subscription in _subscriptions.Values)
        {
            if (!subscription.ShouldReceiveTaskRuntimeStatus())
            {
                continue;
            }

            try
            {
                subscription.Receiver.OnTaskRuntimeStatusChanged(statusMessage);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to dispatch task runtime status to receiver");
            }
        }

        return Task.CompletedTask;
    }

    private sealed class TaskRuntimeEventSubscription(ITaskRuntimeEventReceiver receiver)
    {
        private readonly object _syncRoot = new();
        private readonly ITaskRuntimeEventReceiver _receiver = receiver;
        private readonly HashSet<string> _runIds = new(StringComparer.Ordinal);
        private bool _subscribedToAll;

        public ITaskRuntimeEventReceiver Receiver => _receiver;

        public void SubscribeAll()
        {
            lock (_syncRoot)
            {
                _runIds.Clear();
                _subscribedToAll = true;
            }
        }

        public void SubscribeRunIds(string[] runIds)
        {
            lock (_syncRoot)
            {
                _runIds.Clear();
                _subscribedToAll = false;

                foreach (var runId in runIds)
                {
                    if (string.IsNullOrWhiteSpace(runId))
                    {
                        continue;
                    }

                    _runIds.Add(runId);
                }
            }
        }

        public void Unsubscribe()
        {
            lock (_syncRoot)
            {
                _subscribedToAll = false;
                _runIds.Clear();
            }
        }

        public bool ShouldReceiveJobEvent(string runId)
        {
            lock (_syncRoot)
            {
                if (_subscribedToAll)
                {
                    return true;
                }

                return _runIds.Contains(runId);
            }
        }

        public bool ShouldReceiveTaskRuntimeStatus()
        {
            lock (_syncRoot)
            {
                return _subscribedToAll;
            }
        }
    }
}

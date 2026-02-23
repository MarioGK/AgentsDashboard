using System.Linq;


using MagicOnion.Server.Hubs;
using Microsoft.Extensions.Logging;

namespace AgentsDashboard.TaskRuntime.Features.RuntimeApi.MagicOnion;

/// <summary>
/// StreamingHub for broadcasting job events from task runtime to control plane.
/// The control plane connects as a client and subscribes to specific run IDs.
/// </summary>
public sealed class TaskRuntimeEventHub : StreamingHubBase<ITaskRuntimeEventHub, ITaskRuntimeEventReceiver>, ITaskRuntimeEventHub
{
    private readonly ILogger<TaskRuntimeEventHub> _logger;
    private readonly TaskRuntimeEventDispatcher _dispatcher;
    private Guid _connectionId;

    public TaskRuntimeEventHub(ILogger<TaskRuntimeEventHub> logger, TaskRuntimeEventDispatcher dispatcher)
    {
        _logger = logger;
        _dispatcher = dispatcher;
    }

    protected override ValueTask OnConnecting()
    {
        _connectionId = ConnectionId;
        _dispatcher.RegisterConnection(_connectionId, Client);
        _logger.LogDebug("Client connecting to event hub");
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDisconnected()
    {
        _logger.LogDebug("Client disconnecting from event hub");
        _dispatcher.UnregisterConnection(_connectionId);

        return ValueTask.CompletedTask;
    }

    public Task SubscribeAsync(string[]? runIds = null)
    {
        _dispatcher.Unsubscribe(_connectionId);

        if (runIds == null || runIds.Length == 0)
        {
            _dispatcher.SubscribeAll(_connectionId);
            _logger.LogDebug("Client subscribed to all events");
        }
        else
        {
            _dispatcher.SubscribeRunIds(_connectionId, runIds);
            foreach (var runId in runIds.Where(static runId => !string.IsNullOrWhiteSpace(runId)))
            {
                _logger.LogDebug("Client subscribed to run {RunId}", runId);
            }
        }

        return Task.CompletedTask;
    }

    public Task UnsubscribeAsync()
    {
        _dispatcher.Unsubscribe(_connectionId);

        _logger.LogDebug("Client unsubscribed from all events");

        return Task.CompletedTask;
    }
}

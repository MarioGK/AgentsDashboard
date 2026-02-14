using AgentsDashboard.ControlPlane.Services;
using Microsoft.AspNetCore.SignalR;

namespace AgentsDashboard.ControlPlane.Hubs;

public sealed class RunEventsHub(IOrchestratorMetrics metrics, ILogger<RunEventsHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        metrics.SetSignalRConnections(1);
        logger.LogDebug("SignalR client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        metrics.SetSignalRConnections(-1);
        logger.LogDebug("SignalR client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}

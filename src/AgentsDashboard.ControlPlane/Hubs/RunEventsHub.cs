using AgentsDashboard.ControlPlane.Services;
using Microsoft.AspNetCore.SignalR;

namespace AgentsDashboard.ControlPlane.Hubs;

public sealed class RunEventsHub(IOrchestratorMetrics metrics, ILogger<RunEventsHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        try
        {
            metrics.SetSignalRConnections(1);
            logger.LogDebug("SignalR client connected: {ConnectionId}", Context?.ConnectionId ?? "unknown");
            await base.OnConnectedAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error in OnConnectedAsync");
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            metrics.SetSignalRConnections(-1);
            logger.LogDebug("SignalR client disconnected: {ConnectionId}", Context?.ConnectionId ?? "unknown");
            await base.OnDisconnectedAsync(exception);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error in OnDisconnectedAsync");
        }
    }
}

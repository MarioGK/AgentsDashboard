using AgentsDashboard.ControlPlane.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AgentsDashboard.ControlPlane.Hubs;

/// <summary>
/// SignalR hub for terminal sessions.
/// Provides real-time bidirectional communication between browser clients and worker terminals.
/// </summary>
[Authorize]
public sealed class TerminalHub(ITerminalBridgeService bridgeService, ILogger<TerminalHub> logger) : Hub
{
    /// <summary>
    /// Create a new terminal session on a worker.
    /// </summary>
    /// <param name="workerId">The worker ID to create the session on.</param>
    /// <param name="runId">Optional run ID to associate with the session.</param>
    /// <param name="cols">Initial terminal width in columns.</param>
    /// <param name="rows">Initial terminal height in rows.</param>
    public async Task<string> CreateSession(
        string workerId,
        string? runId,
        int cols,
        int rows)
    {
        try
        {
            var connectionId = Context?.ConnectionId ?? throw new InvalidOperationException("No connection ID");
            logger.LogDebug("Creating terminal session for connection {ConnectionId} on worker {WorkerId}", connectionId, workerId);

            var sessionId = await bridgeService.CreateSessionAsync(
                connectionId,
                workerId,
                runId,
                cols,
                rows,
                Context?.ConnectionAborted ?? CancellationToken.None);

            logger.LogInformation("Terminal session {SessionId} created for connection {ConnectionId}", sessionId, connectionId);
            return sessionId;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create terminal session on worker {WorkerId}", workerId);
            throw;
        }
    }

    /// <summary>
    /// Attach to an existing terminal session.
    /// </summary>
    /// <param name="sessionId">The session ID to attach to.</param>
    /// <param name="lastSequence">The last sequence number the client has received.</param>
    public async Task AttachSession(string sessionId, long lastSequence)
    {
        try
        {
            var connectionId = Context?.ConnectionId ?? throw new InvalidOperationException("No connection ID");
            logger.LogDebug("Attaching connection {ConnectionId} to terminal session {SessionId}", connectionId, sessionId);

            await bridgeService.AttachSessionAsync(
                connectionId,
                sessionId,
                lastSequence,
                Context?.ConnectionAborted ?? CancellationToken.None);

            logger.LogInformation("Connection {ConnectionId} attached to terminal session {SessionId}", connectionId, sessionId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to attach to terminal session {SessionId}", sessionId);
            throw;
        }
    }

    /// <summary>
    /// Send input to a terminal session.
    /// </summary>
    /// <param name="sessionId">The session ID to send input to.</param>
    /// <param name="payloadBase64">Base64-encoded input data.</param>
    public async Task Input(string sessionId, string payloadBase64)
    {
        try
        {
            await bridgeService.SendInputAsync(
                sessionId,
                payloadBase64,
                Context?.ConnectionAborted ?? CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send input to terminal session {SessionId}", sessionId);
            throw;
        }
    }

    /// <summary>
    /// Resize a terminal session.
    /// </summary>
    /// <param name="sessionId">The session ID to resize.</param>
    /// <param name="cols">New width in columns.</param>
    /// <param name="rows">New height in rows.</param>
    public async Task Resize(string sessionId, int cols, int rows)
    {
        try
        {
            await bridgeService.ResizeAsync(
                sessionId,
                cols,
                rows,
                Context?.ConnectionAborted ?? CancellationToken.None);

            logger.LogDebug("Terminal session {SessionId} resized to {Cols}x{Rows}", sessionId, cols, rows);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resize terminal session {SessionId}", sessionId);
            throw;
        }
    }

    /// <summary>
    /// Close a terminal session.
    /// </summary>
    /// <param name="sessionId">The session ID to close.</param>
    public async Task Close(string sessionId)
    {
        try
        {
            await bridgeService.CloseAsync(
                sessionId,
                Context?.ConnectionAborted ?? CancellationToken.None);

            logger.LogInformation("Terminal session {SessionId} closed", sessionId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to close terminal session {SessionId}", sessionId);
            throw;
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var connectionId = Context?.ConnectionId;
        if (connectionId is not null)
        {
            logger.LogDebug("Terminal client disconnected: {ConnectionId}", connectionId);

            try
            {
                await bridgeService.OnClientDisconnectedAsync(connectionId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error cleaning up terminal sessions for disconnected client {ConnectionId}", connectionId);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }
}

namespace AgentsDashboard.ControlPlane.Services;

/// <summary>
/// Bridge service that connects SignalR clients to MagicOnion terminal sessions.
/// This interface is implemented by the terminal bridge service and used by the TerminalHub.
/// </summary>
public interface ITerminalBridgeService
{
    /// <summary>
    /// Create a new terminal session on a worker.
    /// </summary>
    /// <param name="connectionId">The SignalR connection ID of the client.</param>
    /// <param name="workerId">The worker ID to create the session on.</param>
    /// <param name="runId">Optional run ID to associate with the session.</param>
    /// <param name="cols">Initial terminal width in columns.</param>
    /// <param name="rows">Initial terminal height in rows.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session ID of the created terminal session.</returns>
    Task<string> CreateSessionAsync(
        string connectionId,
        string workerId,
        string? runId,
        int cols,
        int rows,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attach to an existing terminal session.
    /// </summary>
    /// <param name="connectionId">The SignalR connection ID of the client.</param>
    /// <param name="sessionId">The session ID to attach to.</param>
    /// <param name="lastSequence">The last sequence number the client has received.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AttachSessionAsync(
        string connectionId,
        string sessionId,
        long lastSequence,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send input to a terminal session.
    /// </summary>
    /// <param name="sessionId">The session ID to send input to.</param>
    /// <param name="payloadBase64">Base64-encoded input data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendInputAsync(
        string sessionId,
        string payloadBase64,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resize a terminal session.
    /// </summary>
    /// <param name="sessionId">The session ID to resize.</param>
    /// <param name="cols">New width in columns.</param>
    /// <param name="rows">New height in rows.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ResizeAsync(
        string sessionId,
        int cols,
        int rows,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Close a terminal session.
    /// </summary>
    /// <param name="sessionId">The session ID to close.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CloseAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when a SignalR client disconnects to clean up any associated sessions.
    /// </summary>
    /// <param name="connectionId">The disconnected connection ID.</param>
    Task OnClientDisconnectedAsync(string connectionId);
}

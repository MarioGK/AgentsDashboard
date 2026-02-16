using MagicOnion;

namespace AgentsDashboard.Contracts.Worker;

/// <summary>
/// StreamingHub interface for bidirectional terminal communication.
/// Workers connect and handle terminal sessions; control plane sends input/resize commands.
/// </summary>
public interface ITerminalHub : IStreamingHub<ITerminalHub, ITerminalReceiver>
{
    /// <summary>
    /// Open a new terminal session on the worker.
    /// </summary>
    Task OpenSessionAsync(OpenTerminalSessionRequest request);

    /// <summary>
    /// Send input to an active terminal session.
    /// </summary>
    Task SendInputAsync(TerminalInputMessage message);

    /// <summary>
    /// Resize an active terminal session.
    /// </summary>
    Task ResizeSessionAsync(TerminalResizeMessage message);

    /// <summary>
    /// Close a terminal session.
    /// </summary>
    Task CloseSessionAsync(CloseTerminalSessionRequest request);

    /// <summary>
    /// Reattach to an existing terminal session to resume receiving output.
    /// </summary>
    Task ReattachSessionAsync(ReattachTerminalSessionRequest request);
}

namespace AgentsDashboard.Contracts.Worker;

/// <summary>
/// Receiver interface for terminal events pushed FROM worker TO control plane.
/// The control plane implements this to receive terminal output callbacks.
/// </summary>
public interface ITerminalReceiver
{
    /// <summary>
    /// Called when a terminal session has been successfully opened.
    /// </summary>
    void OnSessionOpened(TerminalSessionOpenedMessage message);

    /// <summary>
    /// Called when terminal output is available.
    /// </summary>
    void OnSessionOutput(TerminalOutputMessage message);

    /// <summary>
    /// Called when a terminal session has been closed.
    /// </summary>
    void OnSessionClosed(TerminalSessionClosedMessage message);

    /// <summary>
    /// Called when a terminal session encounters an error.
    /// </summary>
    void OnSessionError(TerminalSessionErrorMessage message);
}

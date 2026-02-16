using AgentsDashboard.Contracts.Worker;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class TerminalClientCallbacks
{
    public required Func<TerminalSessionOpenedMessage, Task> OnSessionOpenedAsync { get; init; }
    public required Func<TerminalOutputMessage, Task> OnSessionOutputAsync { get; init; }
    public required Func<TerminalSessionClosedMessage, Task> OnSessionClosedAsync { get; init; }
    public required Func<TerminalSessionErrorMessage, Task> OnSessionErrorAsync { get; init; }
}

public interface ITerminalBridgeService
{
    void RegisterClient(string clientId, TerminalClientCallbacks callbacks);

    Task UnregisterClientAsync(string clientId, CancellationToken cancellationToken = default);

    Task<string> CreateSessionAsync(
        string clientId,
        string? workerId,
        string? runId,
        int cols,
        int rows,
        CancellationToken cancellationToken = default);

    Task AttachSessionAsync(
        string clientId,
        string sessionId,
        long lastSequence,
        CancellationToken cancellationToken = default);

    Task SendInputAsync(
        string sessionId,
        string payloadBase64,
        CancellationToken cancellationToken = default);

    Task ResizeAsync(
        string sessionId,
        int cols,
        int rows,
        CancellationToken cancellationToken = default);

    Task CloseAsync(
        string sessionId,
        CancellationToken cancellationToken = default);
}

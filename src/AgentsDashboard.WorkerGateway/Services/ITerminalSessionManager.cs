using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.WorkerGateway.Services;

public interface ITerminalSessionManager : IDisposable
{
    Task<TerminalSessionInfo> OpenSessionAsync(
        string sessionId,
        string? runId,
        int cols,
        int rows,
        CancellationToken cancellationToken);

    Task SendInputAsync(string sessionId, byte[] data, CancellationToken cancellationToken);

    Task ResizeAsync(string sessionId, int cols, int rows, CancellationToken cancellationToken);

    Task CloseSessionAsync(string sessionId, CancellationToken cancellationToken);

    bool TryGetSession(string sessionId, out TerminalSessionInfo? session);

    void RegisterOutputCallback(string sessionId, Func<byte[], TerminalDataDirection, CancellationToken, Task> callback);

    void UnregisterOutputCallback(string sessionId);
}

public sealed class TerminalSessionInfo
{
    public required string SessionId { get; init; }
    public string? RunId { get; init; }
    public required string ContainerId { get; init; }
    public required string ExecId { get; init; }
    public int Cols { get; set; }
    public int Rows { get; set; }
    public long CurrentSequence;
    public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public bool IsStandaloneContainer { get; init; }
    public CancellationTokenSource Cts { get; } = new();
}

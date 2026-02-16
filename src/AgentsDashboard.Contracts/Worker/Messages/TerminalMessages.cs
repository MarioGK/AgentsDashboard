using AgentsDashboard.Contracts.Domain;
using MessagePack;

namespace AgentsDashboard.Contracts.Worker;

/// <summary>
/// Request to open a new terminal session on a worker.
/// </summary>
[MessagePackObject]
public sealed record OpenTerminalSessionRequest
{
    [Key(0)]
    public required string SessionId { get; init; }

    [Key(1)]
    public required string WorkerId { get; init; }

    [Key(2)]
    public string? RunId { get; init; }

    [Key(3)]
    public int Cols { get; init; } = 80;

    [Key(4)]
    public int Rows { get; init; } = 24;
}

/// <summary>
/// Input message for an active terminal session.
/// Payload is base64-encoded UTF-8 to preserve control characters for TUI rendering.
/// </summary>
[MessagePackObject]
public sealed record TerminalInputMessage
{
    [Key(0)]
    public required string SessionId { get; init; }

    [Key(1)]
    public required string PayloadBase64 { get; init; }
}

/// <summary>
/// Resize message for a terminal session.
/// </summary>
[MessagePackObject]
public sealed record TerminalResizeMessage
{
    [Key(0)]
    public required string SessionId { get; init; }

    [Key(1)]
    public int Cols { get; init; }

    [Key(2)]
    public int Rows { get; init; }
}

/// <summary>
/// Request to close a terminal session.
/// </summary>
[MessagePackObject]
public sealed record CloseTerminalSessionRequest
{
    [Key(0)]
    public required string SessionId { get; init; }
}

/// <summary>
/// Request to reattach to an existing terminal session.
/// </summary>
[MessagePackObject]
public sealed record ReattachTerminalSessionRequest
{
    [Key(0)]
    public required string SessionId { get; init; }

    [Key(1)]
    public long LastSequence { get; init; }
}

/// <summary>
/// Message sent when a terminal session is successfully opened.
/// </summary>
[MessagePackObject]
public sealed record TerminalSessionOpenedMessage
{
    [Key(0)]
    public required string SessionId { get; init; }

    [Key(1)]
    public bool Success { get; init; }

    [Key(2)]
    public string? Error { get; init; }
}

/// <summary>
/// Output message from a terminal session.
/// Payload is base64-encoded UTF-8 to preserve control characters for TUI rendering.
/// </summary>
[MessagePackObject]
public sealed record TerminalOutputMessage
{
    [Key(0)]
    public required string SessionId { get; init; }

    [Key(1)]
    public long Sequence { get; init; }

    [Key(2)]
    public required string PayloadBase64 { get; init; }

    [Key(3)]
    public TerminalDataDirection Direction { get; init; }
}

/// <summary>
/// Message sent when a terminal session has been closed.
/// </summary>
[MessagePackObject]
public sealed record TerminalSessionClosedMessage
{
    [Key(0)]
    public required string SessionId { get; init; }

    [Key(1)]
    public string? Reason { get; init; }
}

/// <summary>
/// Error message from a terminal session.
/// </summary>
[MessagePackObject]
public sealed record TerminalSessionErrorMessage
{
    [Key(0)]
    public required string SessionId { get; init; }

    [Key(1)]
    public required string Error { get; init; }
}

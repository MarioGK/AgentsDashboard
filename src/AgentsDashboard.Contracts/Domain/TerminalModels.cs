namespace AgentsDashboard.Contracts.Domain;

public sealed class TerminalSessionDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WorkerId { get; set; } = string.Empty;
    public string? RunId { get; set; }
    public TerminalSessionState State { get; set; } = TerminalSessionState.Pending;
    public int Cols { get; set; } = 80;
    public int Rows { get; set; } = 24;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAtUtc { get; set; }
    public string? CloseReason { get; set; }
}

public enum TerminalSessionState
{
    Pending = 0,
    Active = 1,
    Disconnected = 2,
    Closed = 3
}

public sealed class TerminalAuditEventDocument
{
    public long Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public TerminalDataDirection Direction { get; set; }
    public string PayloadBase64 { get; set; } = string.Empty;
}

public enum TerminalDataDirection
{
    Input = 0,
    Output = 1
}

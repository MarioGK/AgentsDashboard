namespace AgentsDashboard.Contracts.Domain;

































































public sealed class RunToolProjectionDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RunId { get; set; } = string.Empty;
    public string RepositoryId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public long SequenceStart { get; set; }
    public long SequenceEnd { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string ToolCallId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string InputJson { get; set; } = string.Empty;
    public string OutputJson { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

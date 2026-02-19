namespace AgentsDashboard.Contracts.Domain;

































































public sealed class RunDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RepositoryId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string TaskRuntimeId { get; set; } = string.Empty;
    public RunState State { get; set; } = RunState.Queued;
    public HarnessExecutionMode ExecutionMode { get; set; } = HarnessExecutionMode.Default;
    public string StructuredProtocol { get; set; } = string.Empty;
    public string SessionProfileId { get; set; } = string.Empty;
    public string InstructionStackHash { get; set; } = string.Empty;
    public string McpConfigSnapshotJson { get; set; } = string.Empty;
    public string AutomationRunId { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string OutputJson { get; set; } = string.Empty;
    public int Attempt { get; set; } = 1;
    public string ResultEnvelopeRef { get; set; } = string.Empty;
    public string FailureClass { get; set; } = string.Empty;
    public string PrUrl { get; set; } = string.Empty;
    public string TaskRuntimeImageRef { get; set; } = string.Empty;
    public string TaskRuntimeImageDigest { get; set; } = string.Empty;
    public string TaskRuntimeImageSource { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
}

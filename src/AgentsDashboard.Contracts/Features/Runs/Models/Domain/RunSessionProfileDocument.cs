namespace AgentsDashboard.Contracts.Domain;

































































public sealed class RunSessionProfileDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public RunSessionProfileScope Scope { get; set; } = RunSessionProfileScope.Repository;
    public string RepositoryId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Harness { get; set; } = string.Empty;
    public HarnessExecutionMode ExecutionModeDefault { get; set; } = HarnessExecutionMode.Default;
    public string ApprovalMode { get; set; } = "auto";
    public string DiffViewDefault { get; set; } = "side-by-side";
    public string ToolTimelineMode { get; set; } = "table";
    public string McpConfigJson { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

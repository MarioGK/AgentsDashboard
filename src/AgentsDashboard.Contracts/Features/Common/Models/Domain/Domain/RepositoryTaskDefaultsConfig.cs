namespace AgentsDashboard.Contracts.Domain;

public sealed class RepositoryTaskDefaultsConfig
{
    public TaskKind Kind { get; set; } = TaskKind.OneShot;
    public string Harness { get; set; } = "codex";
    public HarnessExecutionMode ExecutionModeDefault { get; set; } = HarnessExecutionMode.Default;
    public string SessionProfileId { get; set; } = string.Empty;
    public string Command { get; set; } = "echo '{\"status\":\"succeeded\",\"summary\":\"Sample run\",\"artifacts\":[]}'";
    public bool AutoCreatePullRequest { get; set; }
    public bool Enabled { get; set; } = true;
}

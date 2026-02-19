namespace AgentsDashboard.Contracts.Domain;

































































public sealed class WorkflowSynthesisStageConfig
{
    public bool Enabled { get; set; }
    public string Harness { get; set; } = "codex";
    public HarnessExecutionMode Mode { get; set; } = HarnessExecutionMode.Default;
    public string Prompt { get; set; } = string.Empty;
    public string ModelOverride { get; set; } = string.Empty;
    public int? TimeoutSeconds { get; set; }
}
